using MediatR;
using Payment.Domain.Enums;

namespace Payment.Application.Queries;

public record GetPaymentsQuery(Guid LicenseId, int Page = 1, int PageSize = 20)
    : IRequest<GetPaymentsResult>;

public record PaymentDto(
    Guid Id,
    decimal Amount,
    PaymentStatus Status,
    PaymentType Type,
    DateTime CreatedAt,
    DateTime? PaidAt);

public record GetPaymentsResult(
    IReadOnlyList<PaymentDto> Payments,
    int Page,
    int PageSize,
    int Total,
    int TotalPages);
