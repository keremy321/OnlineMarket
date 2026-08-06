using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Application.Services;

/// <summary>
/// Decides whether the storefront layout renders the assistant widget.
/// Visibility depends on <see cref="AiAssistantOptions.Enabled"/> only — never on
/// authentication and never on whether a provider credential is configured.
/// </summary>
public static class AiAssistantWidgetPolicy
{
    /// <summary>
    /// Controllers that intentionally do not host the storefront assistant.
    /// </summary>
    private static readonly HashSet<string> ExcludedControllers = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Admin"
    };

    public static bool ShouldRender(AiAssistantOptions? options, string? controllerName)
    {
        if (options is null || !options.Enabled)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(controllerName)
            || !ExcludedControllers.Contains(controllerName.Trim());
    }
}
