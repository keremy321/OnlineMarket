namespace OnlineMarket.Web.Application.Options;

public class AiAssistantOptions
{
    public const string SectionName = "AiAssistant";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Supported values: "Mock" (built-in rule engine), "OpenAI", "GenericHttp".
    /// </summary>
    public string Provider { get; set; } = "Mock";

    public string? EndpointUrl { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string SystemPrompt { get; set; } = 
        "Sen OnlineMarket e-ticaret platformunun resmi akıllı canlı destek asistanısın. " +
        "Kullanıcılara nazik, yardımsever ve kısa Türkçe yanıtlar ver. " +
        "Ürünler, sipariş takibi, kargo ve ödeme bilgileri konusunda sağlanan bağlamı kullanarak yardımcı ol.";

    public int TimeoutSeconds { get; set; } = 10;

    public double Temperature { get; set; } = 0.7;
}
