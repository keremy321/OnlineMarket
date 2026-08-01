using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Interfaces;

public interface IAiApiClient
{
    Task<AiApiCompletionResponse?> GenerateCompletionAsync(
        AiApiCompletionRequest request,
        CancellationToken cancellationToken = default);
}
