# License Payment System

<div align="center">

A **concurrency-safe, event-driven payment microservice** for a vehicle and driver license platform — built to be fully resilient to race conditions, double-spend, and concurrent payment attempts.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![MassTransit](https://img.shields.io/badge/MassTransit-8.x-red)](https://masstransit.io)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3.x-FF6600?logo=rabbitmq)](https://rabbitmq.com)
[![Redis](https://img.shields.io/badge/Redis-7.x-DC382D?logo=redis)](https://redis.io)
[![SQL Server](https://img.shields.io/badge/SQL_Server-2022-CC2927?logo=microsoftsqlserver)](https://www.microsoft.com/sql-server)
[![React](https://img.shields.io/badge/React-18-61DAFB?logo=react)](https://react.dev)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker)](https://docker.com)

</div>

---

## Overview

This system handles monthly automated billing, real-time external bank callbacks, and balance top-ups for a Georgian vehicle/driver licensing platform — all under concurrent load with guaranteed exactly-once semantics.

**Core problems solved:**

| Problem | Mechanism |
|---|---|
| Two threads debit the same balance simultaneously | `UPDLOCK + ROWLOCK` on both Balance + Payment in one transaction; `CHECK (Amount >= 0)` as hard backstop |
| Bank sends the same payment confirmation twice | `UPDATE WHERE Status='Pending'` — atomic CAS; only one writer gets `rows_affected = 1` |
| Service crashes between DB write and message publish | Transactional outbox — event written in the same commit as the state change |
| Monthly debt generator restarts mid-run | `INSERT WHERE NOT EXISTS` idempotency guard per `(LicenseId, Month)` |
| Mobile client retries a timed-out request | `Idempotency-Key` header → Redis `SET NX PX`; 24 h TTL managed by Redis itself |
| Two concurrent **first-time** requests both execute the same command | Claim-before-execute: `SET NX` Processing sentinel before command; concurrent callers see Processing → 409 + `Retry-After: 2` |
| Pod crash between slot claim and slot complete | Abandoned slot detection: `StatusCode=0` older than 30 s → `DEL` + reclaim on next retry |
| Concurrent balance credit causes MERGE deadlock | UPDATE-then-INSERT loop; SQL 2627/2601 retry; no MERGE anywhere |
| Duplicate saga created for same `(LicenseId, Month)` | Filtered unique index on `MonthlyPaymentStates`; active states only |
| Dead-letter replay triggered twice concurrently | `DeadLetterStatus` enum (`Pending → Replaying → Succeeded`); 409 on duplicate replay |
| Overdue query coupled to saga persistence table | `MarkPaymentOverdueCommand` persists `Overdue` status to Payments; endpoint queries domain table |
| External callback endpoint abused by invalid provider | Fixed-window rate limiter: 100 req / min; no queue |
| Transient SQL Server failover crashes in-flight tx | `EnableRetryOnFailure(5, 30s)` + all transactions wrapped in `ExecutionStrategy` |
| OutboxProcessor silently backlogged | `OutboxLagHealthCheck`: Degraded at 5 min unprocessed; Unhealthy at 30 min; result cached 30 s in Redis |
| Monthly dispatch job aborts on first failure | Per-license try-catch + `LogCritical` + `monthly_debt_dispatch_failures` OTEL metric; loop never aborted |
| Multiple pods run monthly dispatch simultaneously | Redis `SET NX` distributed lock keyed by month; Lua renewal heartbeat; per-license guards as second layer |
| LicenseCore consumers embedded in host project | `LicenseCore.Application` project: consumers + interfaces extracted; `ILicenseRepository` abstracts all DB writes |
| Frontend retries regenerate idempotency key | `useIdempotentMutation` hook: key in `useRef`, generated once per user intent, cleared only on success |
| Insufficient-balance request creates an orphaned Pending payment | `POST /api/payments/quick-pay`: balance checked server-side before creation; insufficient balance creates a `Failed` audit record and returns 422 — no Pending payment is left hanging |

---

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│           React Frontend  :3000                              │
│   useIdempotentMutation + useActivationPolling hooks         │
└──────────────┬──────────────────────────┬────────────────────┘
               │ HTTP + JWT               │ HTTP + JWT
    ┌──────────▼──────────┐    ┌──────────▼──────────────┐
    │   Payment.API :5001  │    │   LicenseCore.API :5002  │
    │   MediatR CQRS       │    │   DebtGenerator          │
    │   OutboxProcessor    │    │   INotificationService   │
    │   OutboxLagHC        │    │   ILicenseRepository     │
    └──────────┬──────────┘    └──────────┬───────────────┘
               │                          │
    ┌──────────▼──────────────────────────▼───────────────┐
    │                  RabbitMQ  :5672                     │
    │         Saga · Consumers · Retry · Dead-letter       │
    └──────────┬───────────────────────┬──────────────────┘
               │                       │
    ┌──────────▼──────────┐   ┌────────▼─────────────────┐
    │     PaymentDb        │   │     LicenseCoreDb         │
    │   SQL Server :1433   │   │     SQL Server :1433      │
    └─────────────────────┘   └──────────────────────────┘
               │                       │
    ┌──────────▼───────────────────────▼───────────────────┐
    │                Redis 7  :6379                         │
    │  Idempotency keys (24 h TTL) · Dispatch lock          │
    │  OutboxLag health cache (30 s TTL)                    │
    └───────────────────────────────────────────────────────┘
```

> For a full technical deep-dive including Mermaid flow diagrams, concurrency proofs, sequence diagrams, and design decision rationale, see **[OVERVIEW.md](./OVERVIEW.md)**.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| ORM | EF Core 10 + SQL Server 2022 |
| Messaging | MassTransit 8 + RabbitMQ 3 |
| CQRS | MediatR 14 |
| Cache / Coordination | Redis 7 (StackExchange.Redis) |
| Auth | JWT Bearer |
| Observability | Serilog → Graylog · OpenTelemetry → Jaeger + Prometheus + Grafana |
| Frontend | React 18 + Vite + TypeScript + Tailwind CSS + React Query |
| Tests | xUnit + Testcontainers (real SQL Server) + FluentAssertions |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)

---

## Quick Start

```bash
# Copy the env template and set your secrets
cp .env.example .env

# Start everything (SQL Server, RabbitMQ, Redis, observability stack, APIs, frontend)
docker compose up --build
```

| Service | URL |
|---|---|
| Payment API | http://localhost:5001/swagger |
| LicenseCore API | http://localhost:5002/swagger |
| Frontend | http://localhost:3000 |
| RabbitMQ Console | http://localhost:15672 `guest / guest` |
| Grafana | http://localhost:3001 `admin / admin` |
| Jaeger | http://localhost:16686 |
| Graylog | http://localhost:9000 `admin / admin` |

---

## Local Development

```bash
# 1. Start only infrastructure
docker compose up sqlserver rabbitmq redis

# 2. Run APIs (separate terminals)
dotnet run --project src/Payment.API
dotnet run --project src/LicenseCore.API

# 3. Frontend
cd Web && npm install && npm run dev
```

---

## Migrations

Migrations run automatically via init containers in Docker Compose. To apply manually:

```bash
dotnet ef database update --project src/Payment.Infrastructure --startup-project src/Payment.API
dotnet ef database update --project src/LicenseCore.API       --startup-project src/LicenseCore.API
```

To run migrations in isolation (e.g. CI/CD):

```bash
dotnet run --project src/Payment.API     -- --migrate
dotnet run --project src/LicenseCore.API -- --migrate
```

---

## Tests

Requires Docker (Testcontainers spins up a real SQL Server container automatically).

```bash
dotnet test src/Payment.Tests
```

| Test suite | What it verifies |
|---|---|
| `ConcurrentBalanceDebitTests` | 10 concurrent tasks × 20 GEL debit on a 100 GEL balance → exactly 5 succeed, balance never goes negative; verifies full atomic `TryDebitAndCompletePaymentAsync` path including Payment row state |
| `DuplicateExternalConfirmTests` | 5 simultaneous bank callbacks → exactly 1 wins, payment idempotent on re-delivery |

---

## API Summary

### Payment.API `:5001`

| Method | Endpoint | Auth | Notes |
|---|---|---|---|
| `POST` | `/api/auth/login` | — | Issue 8 h JWT from `licenseId`; returns `token`, `accountId`, `licenseId` |
| `GET` | `/api/balance/{accountId}` | JWT | Get current balance |
| `POST` | `/api/balance/topup` | JWT | Add funds (UPDATE-then-INSERT, retry-safe) |
| `GET` | `/api/payments/{licenseId}?page&pageSize` | JWT | Paginated history (includes `Failed` records) |
| `POST` | `/api/payments/quick-pay` | JWT | **Preferred**: balance check → create Pending → pay atomically; `Idempotency-Key` required |
| `POST` | `/api/payments/create` | JWT | `Idempotency-Key` header required |
| `POST` | `/api/payments/pay-via-balance` | JWT | `Idempotency-Key` header required |
| `GET` | `/api/payments/overdue?page&pageSize` | JWT | Payments with `Status=Overdue` |
| `POST` | `/api/payments/external/confirm` | HMAC | Bank webhook; rate-limited 100/min |
| `GET` | `/api/admin/dead-letters` | JWT + `role=admin` | Pending failed outbox messages |
| `POST` | `/api/admin/dead-letters/{id}/replay` | JWT + `role=admin` | Re-inject; 409 if already Replaying |
| `GET` | `/health` | — | Liveness (SQL + RabbitMQ + bus topology) |
| `GET` | `/health/ready` | — | Readiness (outbox-lag: Degraded@5min / Unhealthy@30min) |

### LicenseCore.API `:5002`

| Method | Endpoint | Notes |
|---|---|---|
| `GET/POST` | `/api/licenses` | List / create |
| `POST` | `/api/vehicles` | Plate: `^[A-Z0-9-]{1,20}$`, unique |
| `GET/POST` | `/api/drivers` | |

---

## Security Highlights

- **JWT**: startup throws if key < 32 chars or matches known weak placeholder values
- **Webhook**: HMAC-SHA256 `X-Provider-Signature` with constant-time comparison; rate-limited to 100 req/min
- **CORS**: production rejects non-`https://` origins at startup
- **Admin endpoints**: require `role=admin` JWT claim
- **Idempotency**: Redis `SET NX` claims slot before command executes; concurrent callers see Processing → 409 + `Retry-After: 2`; crashed slots auto-recover after 30 s; Redis unavailability returns 503 (never bypasses the guard)
- **Notifications**: faults silently discarded after 10 exponential retries — never pollute the error queue
- **Secrets**: never committed — inject via env vars or secrets manager

---

## License

MIT
