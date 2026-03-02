# License Payment System

<div align="center">

A **concurrency-safe, event-driven payment microservice** for a vehicle and driver license platform — built to be fully resilient to race conditions, double-spend, and concurrent payment attempts.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![MassTransit](https://img.shields.io/badge/MassTransit-9.x-red)](https://masstransit.io)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3.x-FF6600?logo=rabbitmq)](https://rabbitmq.com)
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
| Mobile client retries a timed-out request | `Idempotency-Key` header caches responses for 24 h; expired keys pruned hourly |
| Two concurrent **first-time** requests both execute the same command | Claim-before-execute: INSERT `StatusCode=0` (Processing) before command; concurrent callers see `StatusCode=0` → 409 + `Retry-After: 2` |
| Concurrent balance credit causes MERGE deadlock | UPDATE-then-INSERT loop; SQL 2627/2601 retry; no MERGE anywhere |
| Duplicate saga created for same `(LicenseId, Month)` | Filtered unique index on `MonthlyPaymentStates`; active states only |
| Dead-letter replay triggered twice concurrently | `DeadLetterStatus` enum (`Pending → Replaying → Succeeded`); 409 on duplicate replay |
| Overdue query coupled to saga persistence table | `MarkPaymentOverdueCommand` persists `Overdue` status to Payments; endpoint queries domain table |
| External callback endpoint abused by invalid provider | Fixed-window rate limiter: 100 req / min; no queue |
| Transient SQL Server failover crashes in-flight tx | `EnableRetryOnFailure(5, 30s)` + all transactions wrapped in `ExecutionStrategy` |
| OutboxProcessor silently backlogged | `OutboxLagHealthCheck`: Degraded at 5 min unprocessed; Unhealthy at 30 min |
| Monthly dispatch job aborts on first failure | Per-license try-catch + `LogCritical` + `monthly_debt_dispatch_failures` OTEL metric; loop never aborted |
| LicenseCore consumers embedded in host project | `LicenseCore.Application` project: consumers + interfaces extracted; `ILicenseRepository` abstracts all DB writes |
| Frontend retries regenerate idempotency key | `useIdempotentMutation` hook: key in `useRef`, generated once per user intent, cleared only on success |

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│         React Frontend  :3000                            │
│    useIdempotentMutation hook — stable key per intent    │
└────────────────────┬──────────────────────┬──────────────┘
                     │ HTTP + JWT           │ HTTP + JWT
          ┌──────────▼──────┐    ┌──────────▼──────────┐
          │  Payment.API    │    │  LicenseCore.API    │
          │  :5001          │    │  :5002              │
          │  MediatR CQRS   │    │  DebtGenerator      │
          │  OutboxProcessor│    │  INotificationSvc   │
          │  IKCleanup      │    │  ILicenseRepository │
          │  OutboxLagHC    │    │  (+ Application     │
          └──────────┬──────┘    │   layer project)    │
                     │           └──────────┬──────────┘
          ┌──────────▼──────────────────────▼──────────┐
          │           RabbitMQ  :5672 / :15672          │
          │   Saga · Consumers · Retry · Dead-letter    │
          └──────────┬──────────────────────┬──────────┘
                     │                      │
          ┌──────────▼──────┐    ┌──────────▼──────────┐
          │   PaymentDb     │    │   LicenseCoreDb      │
          │  SQL Server     │    │   SQL Server         │
          └─────────────────┘    └─────────────────────┘
```

> For a full technical deep-dive including Mermaid flow diagrams, concurrency proofs, and design decision rationale, see **[OVERVIEW.md](./OVERVIEW.md)**.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal APIs |
| ORM | EF Core 10 + SQL Server 2022 |
| Messaging | MassTransit 9 + RabbitMQ 3 |
| CQRS | MediatR 14 |
| Auth | JWT Bearer |
| Observability | Serilog → Graylog · OpenTelemetry → Jaeger + Prometheus + Grafana |
| Frontend | React 18 + Vite + TypeScript + Tailwind CSS + React Query |
| Tests | xUnit + Testcontainers (real SQL Server) + FluentAssertions |

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)

---

## Quick Start

```bash
# Copy the env template and set your secrets
cp .env.example .env

# Start everything
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
docker compose up sqlserver rabbitmq

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
dotnet ef database update --project src/Payment.Infrastructure --startup-project src/Payment.Infrastructure
dotnet ef database update --project src/LicenseCore.API       --startup-project src/LicenseCore.API
```

To run migrations in isolation (e.g. CI/CD):

```bash
dotnet run --project src/Payment.API    -- --migrate
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
| `GET` | `/api/balance/{accountId}` | JWT | Get current balance |
| `POST` | `/api/balance/topup` | JWT | Add funds (UPDATE-then-INSERT, retry-safe) |
| `GET` | `/api/payments/{licenseId}?page&pageSize` | JWT | Paginated history |
| `POST` | `/api/payments/create` | JWT | Idempotency-Key required |
| `POST` | `/api/payments/pay-via-balance` | JWT | Idempotency-Key required |
| `GET` | `/api/payments/overdue?page&pageSize` | JWT | Payments with `Status=Overdue` |
| `POST` | `/api/payments/external/confirm` | HMAC | Bank webhook; rate-limited 100/min |
| `GET` | `/api/admin/dead-letters` | JWT + `role=admin` | Pending failed outbox messages |
| `POST` | `/api/admin/dead-letters/{id}/replay` | JWT + `role=admin` | Re-inject; 409 if already Replaying |
| `GET` | `/health` | — | Liveness (SQL + RabbitMQ + bus topology) |
| `GET` | `/health/ready` | — | Readiness (includes outbox-lag: Degraded@5min / Unhealthy@30min) |

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
- **Idempotency**: key slot claimed with `StatusCode=0` BEFORE command executes; concurrent callers get 409 + `Retry-After: 2` until the in-flight request completes
- **Notifications**: faults silently discarded after 10 exponential retries — never pollute the error queue
- **Secrets**: never committed — inject via env vars or secrets manager

---

## License

MIT
