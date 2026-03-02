using FluentAssertions;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Data;
using Payment.Infrastructure.Repositories;
using Payment.Tests.Infrastructure;

namespace Payment.Tests;

[Collection("SqlServer")]
public class DuplicateExternalConfirmTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fixture;

    public DuplicateExternalConfirmTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DuplicateConfirm_OnlyOneSucceeds()
    {
        var paymentId = Guid.NewGuid();
        const string externalId = "EXT-12345";
        const string provider = "TBCBank";

        using (var db = _fixture.CreateDbContext())
        {
            db.Payments.Add(new Domain.Entities.Payment
            {
                Id = paymentId,
                LicenseId = Guid.NewGuid(),
                Amount = 70m,
                Status = PaymentStatus.Pending,
                Type = PaymentType.Monthly,
                ExternalPaymentId = externalId,
                ProviderId = provider,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var paidAt = DateTime.UtcNow;

        var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            using var db = _fixture.CreateDbContext();
            var repo = new PaymentRepository(db);
            var (confirmed, _) = await repo.TryConfirmExternalAtomicallyAsync(externalId, provider, paidAt);
            return confirmed;
        }));

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r);
        successCount.Should().Be(1, "exactly one concurrent confirm should win");

        using (var db = _fixture.CreateDbContext())
        {
            var payment = await db.Payments.FindAsync(paymentId);
            payment!.Status.Should().Be(PaymentStatus.Paid);
        }
    }

    [Fact]
    public async Task ConfirmNonExistentPayment_ReturnsNotConfirmed()
    {
        using var db = _fixture.CreateDbContext();
        var repo = new PaymentRepository(db);

        var (confirmed, paymentId) = await repo.TryConfirmExternalAtomicallyAsync(
            "NON-EXISTENT",
            "BogusBank",
            DateTime.UtcNow);

        confirmed.Should().BeFalse();
        paymentId.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAlreadyPaidPayment_ReturnsNotConfirmed()
    {
        var paymentId = Guid.NewGuid();
        const string externalId = "EXT-ALREADY-PAID";
        const string provider = "BOG";

        using (var db = _fixture.CreateDbContext())
        {
            db.Payments.Add(new Domain.Entities.Payment
            {
                Id = paymentId,
                LicenseId = Guid.NewGuid(),
                Amount = 50m,
                Status = PaymentStatus.Paid,
                Type = PaymentType.AddVehicle,
                ExternalPaymentId = externalId,
                ProviderId = provider,
                PaidAt = DateTime.UtcNow.AddMinutes(-5),
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
            await db.SaveChangesAsync();
        }

        using var dbConfirm = _fixture.CreateDbContext();
        var repo = new PaymentRepository(dbConfirm);
        var (confirmed, _) = await repo.TryConfirmExternalAtomicallyAsync(externalId, provider, DateTime.UtcNow);

        confirmed.Should().BeFalse("idempotent: already-paid payment should not be confirmed again");
    }
}
