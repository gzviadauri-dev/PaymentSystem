using MediatR;
using Payment.Application.Interfaces;
using Payment.Domain.Enums;

namespace Payment.Application.Commands;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, CreatePaymentResult>
{
    private readonly IPaymentRepository _repo;

    public CreatePaymentCommandHandler(IPaymentRepository repo)
    {
        _repo = repo;
    }

    public async Task<CreatePaymentResult> Handle(CreatePaymentCommand cmd, CancellationToken ct)
    {
        if (cmd.Amount <= 0)
            throw new ArgumentException("Payment amount must be positive.");

        var payment = new Domain.Entities.Payment
        {
            Id = Guid.NewGuid(),
            LicenseId = cmd.LicenseId,
            Amount = cmd.Amount,
            Type = cmd.Type,
            Status = PaymentStatus.Pending,
            ExternalPaymentId = cmd.ExternalPaymentId,
            ProviderId = cmd.ProviderId,
            TargetId = cmd.TargetId,
            CreatedAt = DateTime.UtcNow
        };

        await _repo.AddAsync(payment, ct);
        return new CreatePaymentResult(payment.Id, payment.LicenseId);
    }
}
