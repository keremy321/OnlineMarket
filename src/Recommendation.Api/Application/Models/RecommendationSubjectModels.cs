using System.Text.RegularExpressions;

namespace Recommendation.Api.Application.Models;

public static partial class RecommendationSubjectIdContract
{
    public const int MaximumLength = 64;

    public static bool IsValid(string? subjectId)
    {
        return subjectId is not null
            && SubjectIdPattern().IsMatch(subjectId);
    }

    public static bool IsValidVersion(string? version)
    {
        return version is not null
            && VersionPattern().IsMatch(version);
    }

    [GeneratedRegex(
        @"^v[1-9][0-9]{0,14}\.[A-Za-z0-9_-]{43}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SubjectIdPattern();

    [GeneratedRegex(
        @"^v[1-9][0-9]{0,14}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

public sealed record RecommendationSubjectBackfillCandidate(
    Guid OrderId,
    Guid CustomerId,
    string? SubjectId);

public sealed record RecommendationSubjectBackfillResult(
    int ScannedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailureCount);
