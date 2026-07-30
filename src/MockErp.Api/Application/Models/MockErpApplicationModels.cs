using MockErp.Api.Contracts;

namespace MockErp.Api.Application.Models;

public sealed record StoredHttpResponse(
    int StatusCode,
    string Body);

public sealed record ApplicationError(
    int StatusCode,
    string Code,
    string Message,
    bool Retryable);

public sealed record OperationResult(
    StoredHttpResponse? Response,
    ApplicationError? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors)
{
    public bool Succeeded => Response is not null;
}

public sealed record HistoryResult(
    CustomerOrderHistoryResponse? Response,
    ApplicationError? Error)
{
    public bool Succeeded => Response is not null;
}

public sealed record StoreOperationResult(
    StoredHttpResponse? Response,
    ApplicationError? Error);
