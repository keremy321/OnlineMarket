using System.Text;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;

namespace OnlineMarket.Web.Application.Services;

/// <summary>
/// Deterministic Turkish intent router. The known recommendation intents are always
/// resolved in C# so the external provider can never steer a chat message towards a
/// different Recommendation.Api endpoint.
/// </summary>
public sealed class AiIntentRouter : IAiIntentRouter
{
    // Ordered most specific first; the first matching rule wins.
    private static readonly (AiAssistantIntent Intent, string[] Keywords)[] Rules =
    [
        (AiAssistantIntent.CartCompletion,
        [
            "sepetimi tamamla", "sepeti tamamla", "sepetime ne", "sepetime hangi",
            "sepete uygun", "sepetime uygun", "sepetim icin", "sepetime ekle",
            "sepet tamamlama", "bu sepete"
        ]),
        (AiAssistantIntent.FrequentlyBoughtTogether,
        [
            "alanlar baska", "birlikte alin", "birlikte al", "beraber alin",
            "beraber ne", "birlikte ne", "yaninda ne", "yanina ne",
            "birlikte sik", "sikca birlikte", "bununla beraber", "bu urunle beraber"
        ]),
        (AiAssistantIntent.Similar,
        [
            "benzer", "alternatif", "muadil", "buna yakin", "esdeger", "gibi urun"
        ]),
        (AiAssistantIntent.Popular,
        [
            "populer", "en cok satan", "cok satan", "en cok tercih", "trend",
            "gozde", "en begenilen"
        ]),
        (AiAssistantIntent.GeneralRecommendation,
        [
            "urun oner", "bana oner", "oneri", "onerir misin", "oner",
            "tavsiye", "ne almaliyim", "ne alsam", "ne alayim", "bana uygun",
            "ne alabilirim", "bana ne"
        ]),
        (AiAssistantIntent.OrderStatus,
        [
            "siparis", "kargom nerede", "takip", "siparisim nerede"
        ]),
        (AiAssistantIntent.Returns,
        [
            "iade", "iptal", "degisim", "hasarli", "bozuk"
        ]),
        (AiAssistantIntent.Shipping,
        [
            "kargo", "teslimat", "gonderim", "teslim suresi"
        ]),
        (AiAssistantIntent.Payment,
        [
            "odeme", "kredi karti", "taksit", "fatura", "banka karti"
        ]),
        (AiAssistantIntent.Greeting,
        [
            "merhaba", "selam", "gunaydin", "iyi gunler", "iyi aksamlar", "nasilsin"
        ])
    ];

    public AiAssistantIntent Route(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return AiAssistantIntent.Unknown;
        }

        var normalized = Normalize(message);
        foreach (var (intent, keywords) in Rules)
        {
            foreach (var keyword in keywords)
            {
                if (normalized.Contains(keyword, StringComparison.Ordinal))
                {
                    return intent;
                }
            }
        }

        return AiAssistantIntent.Unknown;
    }

    /// <summary>
    /// Folds Turkish diacritics and dotted/dotless "i" so a keyword matches regardless
    /// of how the shopper typed it. Both sides of the comparison use this form.
    /// </summary>
    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var folded = character switch
            {
                'İ' or 'I' or 'ı' or 'i' or 'Î' or 'î' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ü' or 'ü' or 'Û' or 'û' => 'u',
                'Ö' or 'ö' => 'o',
                'Ç' or 'ç' => 'c',
                'Â' or 'â' => 'a',
                _ => char.ToLowerInvariant(character)
            };
            builder.Append(folded);
        }

        return builder.ToString();
    }
}
