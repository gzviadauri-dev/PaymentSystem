using MediatR;
using Payment.Application.Interfaces;

namespace Payment.Application.Queries;

public class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, GetPaymentsResult>
{
    private const int MaxPageSize = 100;

    private readonly IPaymentRepository _paymentRepo;

    public GetPaymentsQueryHandler(IPaymentRepository paymentRepo)
    {
        _paymentRepo = paymentRepo;
    }

    public async Task<GetPaymentsResult> Handle(GetPaymentsQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var payments = await _paymentRepo.GetByLicenseIdAsync(request.LicenseId, page, pageSize, ct);
        var total = await _paymentRepo.CountByLicenseIdAsync(request.LicenseId, ct);
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var dtos = payments.Select(p => new PaymentDto(
            p.Id, p.Amount, p.Status, p.Type, p.CreatedAt, p.PaidAt)).ToList();

        return new GetPaymentsResult(dtos, page, pageSize, total, totalPages);
    }
}
