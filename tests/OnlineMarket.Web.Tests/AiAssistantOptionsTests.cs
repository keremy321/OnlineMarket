using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Tests;

public class AiAssistantOptionsTests
{
    [Fact]
    public void Validate_DefaultConfigurationShape_IsValidWithoutAnApiKey()
    {
        var options = new AiAssistantOptions
        {
            Enabled = true,
            Provider = "OpenAI",
            EndpointUrl = "https://api.openai.com/v1/chat/completions",
            ApiKey = "",
            Model = "gpt-5-nano",
            TimeoutSeconds = 30,
            Temperature = 0.7
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AiAssistantOptions.MaximumTimeoutSeconds + 1)]
    public void Validate_TimeoutOutOfRange_IsRejected(int timeoutSeconds)
    {
        var options = DefaultOpenAiOptions();
        options.TimeoutSeconds = timeoutSeconds;

        Assert.Contains(options.Validate(), error => error.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void Validate_TemperatureOutOfRange_IsRejected(double temperature)
    {
        var options = DefaultOpenAiOptions();
        options.Temperature = temperature;

        Assert.Contains(options.Validate(), error => error.Contains("Temperature", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://api.openai.com/v1/chat/completions")]
    [InlineData("/v1/chat/completions")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_OpenAiProviderWithoutAbsoluteHttpsEndpoint_IsRejected(string? endpointUrl)
    {
        var options = DefaultOpenAiOptions();
        options.EndpointUrl = endpointUrl;

        Assert.Contains(options.Validate(), error => error.Contains("EndpointUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_OpenAiProviderWithBlankModel_IsRejected(string model)
    {
        var options = DefaultOpenAiOptions();
        options.Model = model;

        Assert.Contains(options.Validate(), error => error.Contains("Model", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DisabledAssistant_DoesNotRequireProviderConfiguration()
    {
        var options = new AiAssistantOptions
        {
            Enabled = false,
            Provider = "OpenAI",
            EndpointUrl = null,
            Model = ""
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("BURAYA_API_KEY_YAZIN")]
    [InlineData("YOUR_API_KEY")]
    [InlineData("")]
    [InlineData(null)]
    public void HasProviderCredential_PlaceholderOrBlankKey_IsFalse(string? apiKey)
    {
        var options = DefaultOpenAiOptions();
        options.ApiKey = apiKey;

        Assert.False(options.HasProviderCredential);
        Assert.False(options.CanCallProvider);
    }

    [Fact]
    public void CanCallProvider_WithConfiguredCredential_IsTrue()
    {
        var options = DefaultOpenAiOptions();
        options.ApiKey = "configured-credential";

        Assert.True(options.HasProviderCredential);
        Assert.True(options.CanCallProvider);
    }

    [Fact]
    public void EffectiveValues_AreClampedToSupportedRanges()
    {
        var options = DefaultOpenAiOptions();
        options.MaxMessageLength = 1_000_000;
        options.RecommendationCount = 500;

        Assert.Equal(AiAssistantOptions.MaximumMessageLength, options.EffectiveMaxMessageLength);
        Assert.Equal(AiAssistantOptions.MaximumRecommendationCount, options.EffectiveRecommendationCount);
    }

    private static AiAssistantOptions DefaultOpenAiOptions() => new()
    {
        Enabled = true,
        Provider = "OpenAI",
        EndpointUrl = "https://api.openai.com/v1/chat/completions",
        Model = "gpt-5-nano",
        TimeoutSeconds = 30,
        Temperature = 0.7
    };
}
