using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Interfaces;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;

namespace Recommendation.Api.Infrastructure.Security;

public sealed class HmacRecommendationSubjectIdDeriver(
    IOptions<RecommendationSubjectOptions> options)
    : IRecommendationSubjectIdDeriver
{
    public string Derive(Guid customerId)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty customer identifier is required.",
                nameof(customerId));
        }

        var configuration = options.Value;
        if (!configuration.IsValid())
        {
            throw new InvalidOperationException(
                "Recommendation subject derivation is not configured correctly.");
        }

        var canonicalCustomerId = customerId
            .ToString("D", CultureInfo.InvariantCulture)
            .ToLowerInvariant();
        var keyBytes = Encoding.UTF8.GetBytes(configuration.Key);
        var messageBytes = Encoding.UTF8.GetBytes(
            $"{configuration.Version}:{canonicalCustomerId}");
        try
        {
            var hash = HMACSHA256.HashData(keyBytes, messageBytes);
            var encodedHash = Convert.ToBase64String(hash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var subjectId = $"{configuration.Version}.{encodedHash}";
            if (!RecommendationSubjectIdContract.IsValid(subjectId))
            {
                throw new InvalidOperationException(
                    "Recommendation subject derivation produced an invalid identifier.");
            }

            return subjectId;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(messageBytes);
        }
    }
}
