namespace MockErp.Api.Domain.Entities;

public sealed class ErpIdempotencyRecord
{
    public long Id { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string OperationType { get; set; } = string.Empty;

    public string RequestHash { get; set; } = string.Empty;

    public short ResponseStatusCode { get; set; }

    public string ResponseBody { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public Guid? ResourceId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastAccessedAtUtc { get; set; }
}
