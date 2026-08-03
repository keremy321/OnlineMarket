using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Interfaces;

public interface IAiSupportService
{
    Task<AiSupportResponseDto> ProcessCustomerQueryAsync(
        AiSupportRequestDto request,
        Guid? customerId = null,
        CancellationToken cancellationToken = default);
}
