using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Options;
using Recommendation.Api.Infrastructure.Security;

namespace Recommendation.Api.Tests;

public sealed class RecommendationSubjectIdDeriverTests
{
    private const string FirstKey =
        "test-only-first-subject-derivation-key-with-strong-length";
    private const string SecondKey =
        "test-only-second-subject-derivation-key-with-strong-length";

    [Fact]
    public void Derivation_is_deterministic_and_matches_the_versioned_contract()
    {
        var customerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var deriver = CreateDeriver(FirstKey, "v1");

        var first = deriver.Derive(customerId);
        var second = deriver.Derive(customerId);

        Assert.Equal(first, second);
        Assert.Equal(Expected(customerId, FirstKey, "v1"), first);
        Assert.True(RecommendationSubjectIdContract.IsValid(first));
        Assert.Matches(@"^v1\.[A-Za-z0-9_-]{43}$", first);
    }

    [Fact]
    public void Different_customers_produce_different_subject_ids()
    {
        var deriver = CreateDeriver(FirstKey, "v1");

        var first = deriver.Derive(Guid.Parse(
            "10000000-0000-0000-0000-000000000001"));
        var second = deriver.Derive(Guid.Parse(
            "10000000-0000-0000-0000-000000000002"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Different_keys_and_versions_produce_different_subject_ids()
    {
        var customerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");

        var first = CreateDeriver(FirstKey, "v1").Derive(customerId);
        var changedKey = CreateDeriver(SecondKey, "v1").Derive(customerId);
        var changedVersion = CreateDeriver(FirstKey, "v2").Derive(customerId);

        Assert.NotEqual(first, changedKey);
        Assert.NotEqual(first, changedVersion);
    }

    [Fact]
    public void Empty_customer_id_is_rejected()
    {
        var deriver = CreateDeriver(FirstKey, "v1");

        Assert.Throws<ArgumentException>(() => deriver.Derive(Guid.Empty));
    }

    private static HmacRecommendationSubjectIdDeriver CreateDeriver(
        string key,
        string version)
    {
        return new HmacRecommendationSubjectIdDeriver(
            Options.Create(new RecommendationSubjectOptions
            {
                Key = key,
                Version = version
            }));
    }

    private static string Expected(
        Guid customerId,
        string key,
        string version)
    {
        var message = $"{version}:{customerId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant()}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes(message));
        return $"{version}.{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }
}
