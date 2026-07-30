using System.Security.Cryptography;
using System.Text.Json;

namespace MockErp.Api.Application.Services;

public static class CanonicalRequestHasher
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        WriteIndented = false
    };

    public static string Compute<T>(T request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Options);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
