using System.Text;
using Recommendation.Api.Application.Models;

namespace Recommendation.Api.Application.Options;

public sealed class RecommendationSubjectOptions
{
    public const string SectionName = "RecommendationSubject";
    public const int MinimumKeySizeBytes = 32;
    private const int MaximumKeySizeBytes = 1024;

    public string Key { get; init; } = string.Empty;

    public string Version { get; init; } = "v1";

    public bool IsValid()
    {
        var keySize = Encoding.UTF8.GetByteCount(Key);
        return keySize is >= MinimumKeySizeBytes and <= MaximumKeySizeBytes
            && RecommendationSubjectIdContract.IsValidVersion(Version);
    }
}
