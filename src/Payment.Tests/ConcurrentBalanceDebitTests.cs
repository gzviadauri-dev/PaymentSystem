using FluentAssertions;
using Payment.Application.Interfaces;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Repositories;
using Payment.Tests.Infrastructure;

namespace Payment.Tests;

[Collection("SqlServer")]
public class ConcurrentBalanceDebitTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public ConcurrentBalanceDebitTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentDebits_ShouldNeverGoNegative()
    {
        var accountId = Guid.NewGuid();

        // Create 10 distinct Pending payments — each concurrent task uses its own paymentId.
        var paymentIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        using (var db = _fixture.CreateDbContext())
        {
            db.Balances.Add(new Balance { AccountId = accountId, Amount = 100m, UpdatedAt = DateTime.UtcNow });
            foreach (var pid in paymentIds)
            {
                db.Payments.Add(new Payment.Domain.Entities.Payment
                {
                    Id = pid,
                    LicenseId = accountId,
                    Amount = 20m,
                    Status = PaymentStatus.Pending,
                    Type = PaymentType.Monthly,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        var tasks = paymentIds.Select(pid => Task.Run(async () =>
        {
            using var db = _fixture.CreateDbContext();
            var repo = new BalanceRepository(db);
            var result = await repo.TryDebitAndCompletePaymentAsync(accountId, pid, 20m);
            return result.Status;
        }));

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r == TryDebitResult.Success);
        successCount.Should().Be(5, "only 5 debits of 20 GEL should succeed from a 100 GEL balance");

        using (var db = _fixture.CreateDbContext())
        {
            var balance = await db.Balances.FindAsync(accountId);
            balance!.Amount.Should().Be(0m, "balance should be exactly 0 after 5 successful debits");
            balance.Amount.Should().BeGreaterThanOrEqualTo(0m, "balance must never go negative");
        }
    }

    [Fact]
    public async Task SingleDebit_WhenInsufficientBalance_ReturnsInsufficientBalance()
    {
        var accountId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        using (var db = _fixture.CreateDbContext())
        {
            db.Balances.Add(new Balance { AccountId = accountId, Amount = 10m, UpdatedAt = DateTime.UtcNow });
            db.Payments.Add(new Payment.Domain.Entities.Payment
            {
                Id = paymentId,
                LicenseId = accountId,
                Amount = 50m,
                Status = PaymentStatus.Pending,
                Type = PaymentType.Monthly,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var dbForRepo = _fixture.CreateDbContext();
        var repo = new BalanceRepository(dbForRepo);

        var result = await repo.TryDebitAndCompletePaymentAsync(accountId, paymentId, 50m);

        result.Status.Should().Be(TryDebitResult.InsufficientBalance,
            "cannot debit more than available balance");
        result.CurrentBalance.Should().Be(10m, "current balance should be returned on failure");

        using var dbCheck = _fixture.CreateDbContext();
        var balance = await dbCheck.Balances.FindAsync(accountId);
        balance!.Amount.Should().Be(10m, "balance should be unchanged after failed debit");
    }

    [Fact]
    public async Task TopUp_ThenDebit_BalanceIsCorrect()
    {
        var accountId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        using (var db = _fixture.CreateDbContext())
        {
            db.Payments.Add(new Payment.Domain.Entities.Payment
            {
                Id = paymentId,
                LicenseId = accountId,
                Amount = 70m,
                Status = PaymentStatus.Pending,
                Type = PaymentType.Monthly,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var db2 = _fixture.CreateDbContext();
        var repo = new BalanceRepository(db2);

        await repo.CreditAsync(accountId, 150m);
        var result = await repo.TryDebitAndCompletePaymentAsync(accountId, paymentId, 70m);

        result.Status.Should().Be(TryDebitResult.Success);

        var balance = await repo.GetByAccountIdAsync(accountId);
        balance!.Amount.Should().Be(80m);
    }
}
