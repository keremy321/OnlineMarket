using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Application.Services;

public class AiSupportService : IAiSupportService
{
    private readonly ICatalogService _catalogService;
    private readonly IOrderService _orderService;
    private readonly IRecommendationClient _recommendationClient;
    private readonly IAiApiClient? _aiApiClient;
    private readonly IOptions<AiAssistantOptions>? _options;
    private readonly ILogger<AiSupportService> _logger;

    public AiSupportService(
        ICatalogService catalogService,
        IOrderService orderService,
        IRecommendationClient recommendationClient,
        ILogger<AiSupportService> logger)
        : this(catalogService, orderService, recommendationClient, null, null, logger)
    {
    }

    public AiSupportService(
        ICatalogService catalogService,
        IOrderService orderService,
        IRecommendationClient recommendationClient,
        IAiApiClient? aiApiClient,
        IOptions<AiAssistantOptions>? options,
        ILogger<AiSupportService> logger)
    {
        _catalogService = catalogService;
        _orderService = orderService;
        _recommendationClient = recommendationClient;
        _aiApiClient = aiApiClient;
        _options = options;
        _logger = logger;
    }

    public async Task<AiSupportResponseDto> ProcessCustomerQueryAsync(
        AiSupportRequestDto request,
        Guid? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId;

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return new AiSupportResponseDto(
                Reply: "Lütfen sormak istediğiniz soruyu yazın. Size ürünlerimiz, siparişleriniz veya kargo süreçleriniz hakkında yardımcı olabilirim! 🤖",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions());
        }

        // Try External LLM API if configured
        var apiResult = await TryProcessWithExternalApiAsync(request, conversationId, customerId, cancellationToken);
        if (apiResult != null)
        {
            return apiResult;
        }

        // Fallback to Rule-based Assistant Engine
        var message = request.Message.Trim().ToLowerInvariant();

        try
        {
            // 1. Order tracking & status
            if (message.Contains("sipariş") || message.Contains("kargom") || message.Contains("nerede") || message.Contains("takip"))
            {
                return await HandleOrderQueryAsync(conversationId, customerId);
            }

            // 2. Shipping & cargo policy
            if (message.Contains("kargo") || message.Contains("teslimat") || message.Contains("ücret") || message.Contains("gönderim"))
            {
                return new AiSupportResponseDto(
                    Reply: "🚀 **Kargo ve Teslimat Bilgileri**:\n- **500 ₺ ve üzeri** siparişlerinizde **Ücretsiz Ekspres Kargo** uygulanır.\n- 500 ₺ altı siparişlerde standart kargo ücreti 49.90 ₺'dir.\n- Siparişleriniz aynı gün özenle paketlenip kargoya teslim edilmektedir.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Sipariş takibi", "Popüler ürünler", "İade koşulları" });
            }

            // 3. Payment methods & invoicing
            if (message.Contains("ödeme") || message.Contains("kredi kartı") || message.Contains("taksit") || message.Contains("fatura") || message.Contains("banka"))
            {
                return new AiSupportResponseDto(
                    Reply: "💳 **Ödeme ve Fatura**:\n- Tüm Kredi Kartı ve Banka Kartları ile güvenle ödeme yapabilirsiniz.\n- Ödemeleriniz OWASP güvenlik standartlarında 256-bit SSL korumalı altyapı ile gerçekleşir.\n- Sipariş faturanız Uyumsoft ERP entegrasyonu ile otomatik olarak oluşturulup e-posta adresinize gönderilir.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Kargo ücretleri nedir?", "Popüler ürünler" });
            }

            // 4. Returns & cancellation
            if (message.Contains("iade") || message.Contains("iptal") || message.Contains("değişim") || message.Contains("hasarlı") || message.Contains("bozuk"))
            {
                return new AiSupportResponseDto(
                    Reply: "🔄 **İade ve Değişim Politikamız**:\n- Ürünlerinizi teslim aldıktan sonra **14 gün** içerisinde iade edebilirsiniz.\n- Taze gıda ve soğuk zincir ürünlerinde hasarlı teslimat durumunda koşulsuz anında değişim yapılmaktadır.\n- İade sürecini Hesabım > Siparişlerim sayfasından başlatabilirsiniz.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Sipariş takibi", "Müşteri Hizmetleri" });
            }

            // 5. Popular recommendations request
            if (message.Contains("tavsiye") || message.Contains("öneri") || message.Contains("popüler") || message.Contains("en çok satan") || message.Contains("trend"))
            {
                return await HandleRecommendationQueryAsync(conversationId);
            }

            // 6. Product search / catalog lookup query
            var isProductSearch = message.Contains("ürün") || message.Contains("meyve") || message.Contains("sebze") ||
                                  message.Contains("süt") || message.Contains("organik") || message.Contains("fiyat") ||
                                  message.Contains("stok") || message.Contains("var mı") || message.Contains("ara");

            if (isProductSearch || message.Length > 2)
            {
                var productSearchReply = await HandleProductSearchQueryAsync(request.Message, conversationId);
                if (productSearchReply != null)
                {
                    return productSearchReply;
                }
            }

            // 7. General greeting & default response
            return new AiSupportResponseDto(
                Reply: "Merhaba! ben **OnlineMarket Akıllı AI Asistanı** 🤖\nSize nasıl yardımcı olabilirim? Aşağıdaki hızlı konulardan birini seçebilir veya sormak istediğiniz ürünü/konuyu doğrudan yazabilirsiniz.",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AI support query: {Message}", request.Message);
            return new AiSupportResponseDto(
                Reply: "Şu anda isteğinizi işlerken geçici bir aksaklık oluştu. Lütfen tekrar deneyin veya müşteri destek ekibimizle iletişime geçin.",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions(),
                Success: false,
                ErrorMessage: ex.Message);
        }
    }

    private async Task<AiSupportResponseDto?> TryProcessWithExternalApiAsync(
        AiSupportRequestDto request,
        string conversationId,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        if (_aiApiClient == null || _options?.Value == null) return null;

        var config = _options.Value;
        var provider = config.Provider?.Trim() ?? "";
        var endpoint = config.EndpointUrl?.Trim() ?? "";
        var apiKey = config.ApiKey?.Trim() ?? "";

        var isPlaceholder = provider.Contains("BURAYA", StringComparison.OrdinalIgnoreCase) ||
                            provider.Contains("PROVIDER", StringComparison.OrdinalIgnoreCase) ||
                            endpoint.Contains("BURAYA", StringComparison.OrdinalIgnoreCase) ||
                            endpoint.Contains("ENDPOINT", StringComparison.OrdinalIgnoreCase) ||
                            apiKey.Contains("BURAYA", StringComparison.OrdinalIgnoreCase) ||
                            apiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);

        if (!config.Enabled || 
            string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase) || 
            string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            isPlaceholder)
        {
            return null;
        }

        try
        {
            // Build rich context prompt for LLM API
            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine(config.SystemPrompt);

            // Add Order Context if customer logged in
            if (customerId.HasValue)
            {
                var orders = await _orderService.GetCustomerOrdersAsync(customerId.Value);
                var latestOrder = orders.OrderByDescending(o => o.PlacedAtUtc).FirstOrDefault();
                if (latestOrder != null)
                {
                    contextBuilder.AppendLine($"\n[Müşteri Sipariş Bilgisi]: Son Sipariş No: {latestOrder.OrderNumber}, Tarih: {latestOrder.PlacedAtUtc:dd.MM.yyyy HH:mm}, Tutar: {latestOrder.GrandTotal:N2} TL.");
                }
            }

            // Add Catalog Context if query might relate to products
            var filter = new ProductFilterDto(null, null, request.Message.Trim(), null, null, true, "newest");
            var catalogMatches = await _catalogService.GetProductsAsync(filter);
            if (catalogMatches.Any())
            {
                var topMatches = catalogMatches.Take(3).Select(p => $"{p.Name} ({p.Price:N2} TL, Kategori: {p.CategoryName})");
                contextBuilder.AppendLine($"\n[Katalog İlgili Ürünler]: {string.Join("; ", topMatches)}");
            }

            var messages = new List<AiChatMessage>
            {
                new("system", contextBuilder.ToString()),
                new("user", request.Message)
            };

            var apiRequest = new AiApiCompletionRequest(
                Model: config.Model,
                Messages: messages,
                Temperature: config.Temperature);

            var apiResponse = await _aiApiClient.GenerateCompletionAsync(apiRequest, cancellationToken);
            var choice = apiResponse?.Choices?.FirstOrDefault();

            if (choice != null && !string.IsNullOrWhiteSpace(choice.Message?.Content))
            {
                return new AiSupportResponseDto(
                    Reply: choice.Message.Content.Trim(),
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: GetDefaultSuggestions());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AI completion from external API provider. Falling back to rule engine.");
        }

        return null;
    }

    private async Task<AiSupportResponseDto> HandleOrderQueryAsync(string conversationId, Guid? customerId)
    {
        if (!customerId.HasValue)
        {
            return new AiSupportResponseDto(
                Reply: "🔒 Sipariş takibi yapabilmem için öncelikle hesabınıza **Giriş Yapmış** olmanız gerekmektedir. Giriş yaptıktan sonra son siparişlerinizin durumunu anlık olarak buradan kontrol edebilirsiniz.",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: new List<string> { "Kargo ücretleri nedir?", "Popüler ürünler" });
        }

        var orders = await _orderService.GetCustomerOrdersAsync(customerId.Value);
        if (!orders.Any())
        {
            return new AiSupportResponseDto(
                Reply: "Henüz verilmiş bir siparişiniz bulunmamaktadır. Taze ve kaliteli market ürünlerimizi keşfetmek için kataloğumuza göz atabilirsiniz! 🛒",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: new List<string> { "Popüler ürünler", "Kargo ücretleri nedir?" });
        }

        var latestOrder = orders.OrderByDescending(o => o.PlacedAtUtc).First();
        var itemCount = latestOrder.Items.Sum(i => i.Quantity);
        var dateFormatted = latestOrder.PlacedAtUtc.ToString("dd.MM.yyyy HH:mm");

        var replyText = $"📦 **Son Siparişiniz**: `{latestOrder.OrderNumber}`\n" +
                        $"- **Tarih**: {dateFormatted}\n" +
                        $"- **Ürün Sayısı**: {itemCount} adet\n" +
                        $"- **Toplam Tutar**: {latestOrder.GrandTotal:N2} ₺\n" +
                        $"- **Sipariş Durumu**: Hazırlanıyor / Kargoda 🚚\n\n" +
                        $"Tüm sipariş geçmişinize **Hesabım > Siparişlerim** menüsünden ulaşabilirsiniz.";

        return new AiSupportResponseDto(
            Reply: replyText,
            ConversationId: conversationId,
            TimestampUtc: DateTime.UtcNow,
            SuggestedActions: new List<string> { "Kargo ücretleri nedir?", "Popüler ürünler", "İade koşulları" });
    }

    private async Task<AiSupportResponseDto> HandleRecommendationQueryAsync(string conversationId)
    {
        var recs = await _recommendationClient.GetPopularRecommendationsAsync(3);
        if (recs.Any())
        {
            var productIds = recs.Select(r => r.ProductId).Distinct();
            var productsDict = await _catalogService.GetProductsByIdsAsync(productIds);

            var itemsList = new List<string>();
            foreach (var r in recs)
            {
                if (productsDict.TryGetValue(r.ProductId, out var product) && product.IsActive)
                {
                    itemsList.Add($"• **{product.Name}** — {product.Price:N2} ₺ ({product.CategoryName})");
                }
            }

            if (itemsList.Any())
            {
                var reply = "🌟 **Bugünün En Popüler Ürün Önerileri**:\n" +
                            string.Join("\n", itemsList) +
                            "\n\nSiz de bu ürünleri sepetinize ekleyerek fırsatları kaçırmayın!";

                return new AiSupportResponseDto(
                    Reply: reply,
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Kargo ücretleri nedir?", "Sipariş takibi" });
            }
        }

        return new AiSupportResponseDto(
            Reply: "Sizlere özel akıllı öneri motorumuz arka planda en taze ve popüler ürünleri hazırlıyor. Kataloğumuzdan güncel ürünlerimizi inceleyebilirsiniz!",
            ConversationId: conversationId,
            TimestampUtc: DateTime.UtcNow,
            SuggestedActions: GetDefaultSuggestions());
    }

    private async Task<AiSupportResponseDto?> HandleProductSearchQueryAsync(string query, string conversationId)
    {
        var cleanQuery = query.Replace("var mı", "")
                              .Replace("ürünler", "")
                              .Replace("nelerdir", "")
                              .Replace("fiyatı", "")
                              .Trim();

        if (cleanQuery.Length < 2) return null;

        var filter = new ProductFilterDto(
            CategoryId: null,
            BrandId: null,
            SearchQuery: cleanQuery,
            MinPrice: null,
            MaxPrice: null,
            InStockOnly: true,
            SortBy: "newest");

        var products = await _catalogService.GetProductsAsync(filter);
        if (!products.Any())
        {
            // Try fetching all products to see if category matches
            filter = new ProductFilterDto(null, null, null, null, null, true, "newest");
            var allProducts = await _catalogService.GetProductsAsync(filter);
            products = allProducts
                .Where(p => p.Name.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                            p.CategoryName.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                            p.BrandName.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (products.Any())
        {
            var topProducts = products.Take(3).ToList();
            var productLines = topProducts.Select(p => $"• **{p.Name}** — {p.Price:N2} ₺ ({p.BrandName} / {p.CategoryName})");

            var replyText = $"🔍 **\"{cleanQuery}\" Aramanız İçin Bulunan Ürünler**:\n" +
                            string.Join("\n", productLines) +
                            (products.Count > 3 ? $"\n\n*ve {products.Count - 3} ürün daha mevcut!*" : "") +
                            $"\n\nTüm sonuçları kataloğumuzda inceleyebilirsiniz.";

            return new AiSupportResponseDto(
                Reply: replyText,
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: new List<string> { "Popüler ürünler", "Kargo ücretleri nedir?", "Sipariş takibi" });
        }

        return null;
    }

    private static List<string> GetDefaultSuggestions()
    {
        return new List<string>
        {
            "Kargo ücretleri nedir?",
            "Sipariş takibi",
            "Popüler ürün önerileri",
            "İade ve Değişim"
        };
    }
}
