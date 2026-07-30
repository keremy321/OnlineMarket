using System.Security.Cryptography;
using System.Text.Json;
using ErpIntegration.Api.Contracts;

namespace ErpIntegration.Api.Application.Services;

public static class CanonicalPayloadHasher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        WriteIndented = false
    };

    public static string Compute(OrderReadyForErpV1Request normalizedRequest)
    {
        ArgumentNullException.ThrowIfNull(normalizedRequest);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            normalizedRequest,
            SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(payload))
            .ToLowerInvariant();
    }
}
