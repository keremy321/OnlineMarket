using System.Reflection;
using System.Text.RegularExpressions;
using OnlineMarket.Web.Application.Options;
using OnlineMarket.Web.Application.Services;

namespace OnlineMarket.Web.Tests;

/// <summary>
/// Guards the regression that removed the launcher from the storefront: the widget used
/// to render only for signed-in shoppers. Visibility must follow AiAssistant:Enabled
/// alone.
/// </summary>
public class AiAssistantWidgetVisibilityTests
{
    // 1. Enabled = true renders the launcher.
    [Theory]
    [InlineData("Home")]
    [InlineData("Catalog")]
    [InlineData("Cart")]
    [InlineData("Checkout")]
    [InlineData("Orders")]
    [InlineData(null)]
    public void ShouldRender_WhenEnabled_IsTrueOnStorefrontPages(string? controllerName)
    {
        var options = new AiAssistantOptions { Enabled = true };

        Assert.True(AiAssistantWidgetPolicy.ShouldRender(options, controllerName));
    }

    // 2. Enabled = false does not render it.
    [Fact]
    public void ShouldRender_WhenDisabled_IsFalse()
    {
        var options = new AiAssistantOptions { Enabled = false };

        Assert.False(AiAssistantWidgetPolicy.ShouldRender(options, "Home"));
    }

    // 3. A blank OpenAI ApiKey must never hide the widget.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldRender_WithBlankApiKey_IsStillTrue(string? apiKey)
    {
        var options = new AiAssistantOptions
        {
            Enabled = true,
            Provider = "OpenAI",
            EndpointUrl = "https://api.openai.com/v1/chat/completions",
            ApiKey = apiKey
        };

        Assert.True(AiAssistantWidgetPolicy.ShouldRender(options, "Home"));
        Assert.False(options.HasProviderCredential);
        Assert.False(options.CanCallProvider);
    }

    // 4. The admin area intentionally does not host the storefront assistant.
    [Fact]
    public void ShouldRender_OnAdminPages_IsFalse()
    {
        var options = new AiAssistantOptions { Enabled = true };

        Assert.False(AiAssistantWidgetPolicy.ShouldRender(options, "Admin"));
    }

    // 5. Visibility cannot depend on the shopper being authenticated: the decision has no
    //    access to a user at all.
    [Fact]
    public void ShouldRender_HasNoUserOrAuthenticationParameter()
    {
        var method = typeof(AiAssistantWidgetPolicy)
            .GetMethod(nameof(AiAssistantWidgetPolicy.ShouldRender), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(
            new[] { typeof(AiAssistantOptions), typeof(string) },
            method!.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }

    // 4. The CSS and JavaScript references appear exactly once in the shared layout, and
    //    the widget is not gated on authentication any more.
    [Fact]
    public void SharedLayout_ReferencesWidgetAssetsExactlyOnceAndDoesNotGateOnAuthentication()
    {
        var layout = ReadWebFile(Path.Combine("Views", "Shared", "_Layout.cshtml"));

        Assert.Equal(1, CountOccurrences(layout, "~/css/ai-widget.css"));
        Assert.Equal(1, CountOccurrences(layout, "~/js/ai-chat.js"));
        Assert.Equal(1, CountOccurrences(layout, "_AiSupportWidget"));
        Assert.Contains("AiAssistantWidgetPolicy.ShouldRender", layout, StringComparison.Ordinal);

        // The script tag must defer so initialization runs after the widget markup exists.
        Assert.Matches(new Regex(@"<script[^>]*src=""~/js/ai-chat\.js""[^>]*\sdefer"), layout);

        // Every ai-chat.js / _AiSupportWidget reference must be guarded by showAiAssistant.
        foreach (var line in layout.Split('\n'))
        {
            if (line.Contains("ai-chat.js", StringComparison.Ordinal)
                || line.Contains("_AiSupportWidget", StringComparison.Ordinal)
                || line.Contains("ai-widget.css", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("IsAuthenticated", line, StringComparison.Ordinal);
            }
        }

        var guardedRegion = layout[layout.IndexOf("showAiAssistant", StringComparison.Ordinal)..];
        Assert.Contains("showAiAssistant", guardedRegion, StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetPartial_ExposesNoSystemPromptOrCredential()
    {
        var partial = ReadWebFile(Path.Combine("Views", "Shared", "_AiSupportWidget.cshtml"));
        var defaults = new AiAssistantOptions();

        Assert.DoesNotContain("ApiKey", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SystemPrompt", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EndpointUrl", partial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(defaults.SystemPrompt, partial, StringComparison.Ordinal);

        // The launcher is keyboard reachable and labelled.
        Assert.Contains("aria-label=\"AI destek asistanını aç\"", partial, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", partial, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettings_DoNotContainAnAiAssistantCredential()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var content = ReadWebFile(fileName);
            using var document = System.Text.Json.JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("AiAssistant", out var section))
            {
                continue;
            }

            if (section.TryGetProperty("ApiKey", out var apiKey))
            {
                Assert.True(
                    string.IsNullOrWhiteSpace(apiKey.GetString()),
                    $"{fileName} must not contain an AiAssistant API key.");
            }
        }
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = content.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = content.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string ReadWebFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "OnlineMarket.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var fullPath = Path.Combine(directory!.FullName, "src", "OnlineMarket.Web", relativePath);
        Assert.True(File.Exists(fullPath), $"Expected '{fullPath}' to exist.");
        return File.ReadAllText(fullPath);
    }
}
