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
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ILogger<AiSupportService> _logger;

    public AiSupportService(
        ICatalogService catalogService,
        IOrderService orderService,
        IRecommendationClient recommendationClient,
        ILogger<AiSupportService> logger)
        : this(catalogService, orderService, recommendationClient, null, null, new InMemoryChatHistoryStore(), logger)
    {
    }

    public AiSupportService(
        ICatalogService catalogService,
        IOrderService orderService,
        IRecommendationClient recommendationClient,
        IAiApiClient? aiApiClient,
        IOptions<AiAssistantOptions>? options,
        ILogger<AiSupportService> logger)
        : this(catalogService, orderService, recommendationClient, aiApiClient, options, new InMemoryChatHistoryStore(), logger)
    {
    }

    public AiSupportService(
        ICatalogService catalogService,
        IOrderService orderService,
        IRecommendationClient recommendationClient,
        IAiApiClient? aiApiClient,
        IOptions<AiAssistantOptions>? options,
        IChatHistoryStore chatHistoryStore,
        ILogger<AiSupportService> logger)
    {
        _catalogService = catalogService;
        _orderService = orderService;
        _recommendationClient = recommendationClient;
        _aiApiClient = aiApiClient;
        _options = options;
        _chatHistoryStore = chatHistoryStore ?? new InMemoryChatHistoryStore();
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

        // Sync client-provided history if available
        if (request.History != null && request.History.Count > 0)
        {
            var existingHistory = GetHistory(customerId, conversationId);
            if (existingHistory.Count == 0)
            {
                foreach (var msg in request.History)
                {
                    AddHistoryMessage(customerId, conversationId, msg.Sender, msg.Text);
                }
            }
        }

        // Safe message truncation (OWASP LLM10: DoS Prevention)
        var sanitizedInput = request.Message.Trim();
        if (sanitizedInput.Length > 1000)
        {
            sanitizedInput = sanitizedInput[..1000];
        }

        // OWASP Pre-filtering: Prompt Injection & Source Code leakage protection
        if (IsSecurityViolationQuery(sanitizedInput))
        {
            var refusalResponse = new AiSupportResponseDto(
                Reply: "Güvenlik politikalarımız gereği kaynak kodları, sistem mimarisi veya teknik yapılandırmalar paylaşılamaz. Yalnızca OnlineMarket ürünleri, sipariş takibi, kargo ve ödeme süreçleri hakkında yardımcı olabilirim. Size bu konularda nasıl yardımcı olabilirim? 🛒",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions());

            AddHistoryMessage(customerId, conversationId, "user", sanitizedInput);
            AddHistoryMessage(customerId, conversationId, "assistant", refusalResponse.Reply);
            return refusalResponse;
        }

        // Record user message into chat history store
        AddHistoryMessage(customerId, conversationId, "user", sanitizedInput);

        var sanitizedRequest = request with { Message = sanitizedInput };

        // Try External LLM API if configured
        var apiResult = await TryProcessWithExternalApiAsync(sanitizedRequest, conversationId, customerId, cancellationToken);
        if (apiResult != null)
        {
            AddHistoryMessage(customerId, conversationId, "assistant", apiResult.Reply);
            return apiResult;
        }

        // Fallback to Rule-based Assistant Engine
        var message = sanitizedInput.ToLowerInvariant();
        AiSupportResponseDto response;

        try
        {
            // 1. Context-aware follow-up handling (e.g. "seç", "ürünleri seç", "evet", "tamam")
            var lastAssistantMsg = GetLastAssistantMessage(customerId, conversationId);
            if (IsFollowUpSelectionIntent(message) && lastAssistantMsg != null)
            {
                var selectionResponse = await HandleContextualSelectionAsync(lastAssistantMsg.Text, conversationId);
                if (selectionResponse != null)
                {
                    AddHistoryMessage(customerId, conversationId, "assistant", selectionResponse.Reply);
                    return selectionResponse;
                }
            }

            // 2. Meal / Dinner Planning query
            if (message.Contains("yemek") || message.Contains("akşam") || message.Contains("menü") || message.Contains("planla"))
            {
                response = new AiSupportResponseDto(
                    Reply: "Merhaba! Bugün akşam için hızlı ve lezzetli bir menü önerisi: **Izgara Tavuk Göğsü**, **Haşlanmış Sebzeler** ve **Taze Yoğurtlu Salata**. 🍗🥗\nİsterseniz katalogdan bu menüye uygun ürünleri seçip listeleyebilirim!",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Ürünleri seç", "Popüler ürünler", "Kargo ücretleri nedir?" });

                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 3. Order tracking & status
            if (message.Contains("sipariş") || message.Contains("kargom") || message.Contains("nerede") || message.Contains("takip"))
            {
                response = await HandleOrderQueryAsync(conversationId, customerId);
                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 4. Shipping & cargo policy
            if (message.Contains("kargo") || message.Contains("teslimat") || message.Contains("ücret") || message.Contains("gönderim"))
            {
                response = new AiSupportResponseDto(
                    Reply: "🚀 500 ₺ ve üzeri siparişlerinizde Ekspres Kargo ücretsizdir; 500 ₺ altı siparişlerde standart kargo ücreti 49.90 ₺'dir.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Sipariş takibi", "Popüler ürünler", "İade koşulları" });

                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 5. Payment methods & invoicing
            if (message.Contains("ödeme") || message.Contains("kredi kartı") || message.Contains("taksit") || message.Contains("fatura") || message.Contains("banka"))
            {
                response = new AiSupportResponseDto(
                    Reply: "💳 Tüm kredi ve banka kartları ile 256-bit SSL korumalı altyapımız üzerinden güvenle ödeme yapabilirsiniz.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Kargo ücretleri nedir?", "Popüler ürünler" });

                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 6. Returns & cancellation
            if (message.Contains("iade") || message.Contains("iptal") || message.Contains("değişim") || message.Contains("hasarlı") || message.Contains("bozuk"))
            {
                response = new AiSupportResponseDto(
                    Reply: "🔄 Ürünlerinizi teslim aldıktan sonra 14 gün içerisinde Hesabım > Siparişlerim sayfasından kolayca iade edebilirsiniz.",
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Sipariş takibi", "Müşteri Hizmetleri" });

                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 7. Popular recommendations request
            if (message.Contains("tavsiye") || message.Contains("öneri") || message.Contains("popüler") || message.Contains("en çok satan") || message.Contains("trend"))
            {
                response = await HandleRecommendationQueryAsync(conversationId);
                AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
                return response;
            }

            // 8. Product search / catalog lookup query
            var isProductSearch = message.Contains("ürün") || message.Contains("meyve") || message.Contains("sebze") ||
                                  message.Contains("süt") || message.Contains("organik") || message.Contains("fiyat") ||
                                  message.Contains("stok") || message.Contains("var mı") || message.Contains("ara");

            if (isProductSearch || message.Length > 2)
            {
                var productSearchReply = await HandleProductSearchQueryAsync(request.Message, conversationId);
                if (productSearchReply != null)
                {
                    AddHistoryMessage(customerId, conversationId, "assistant", productSearchReply.Reply);
                    return productSearchReply;
                }
            }

            // 9. General greeting & default response
            response = new AiSupportResponseDto(
                Reply: "Merhaba! OnlineMarket Akıllı AI Asistanıyım, size ürünlerimiz, siparişleriniz veya kargo süreçlerinizde nasıl yardımcı olabilirim? 🤖",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions());

            AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
            return response;
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

    private void AddHistoryMessage(Guid? accountId, string conversationId, string sender, string text)
    {
        if (accountId.HasValue)
        {
            _chatHistoryStore.AddMessage(accountId.Value, conversationId, sender, text);
        }
    }

    private List<AiChatMessageDto> GetHistory(Guid? accountId, string conversationId)
    {
        return accountId.HasValue
            ? _chatHistoryStore.GetHistory(accountId.Value, conversationId)
            : new List<AiChatMessageDto>();
    }

    private AiChatMessageDto? GetLastAssistantMessage(Guid? accountId, string conversationId)
    {
        return accountId.HasValue
            ? _chatHistoryStore.GetLastAssistantMessage(accountId.Value, conversationId)
            : null;
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

            contextBuilder.AppendLine("\n[MUTLAK KURAL - KISA CEVAP ZORUNLULUĞU]:");
            contextBuilder.AppendLine("Yanıtın KESİNLİKLE madde işareti (•, -), liste, alt başlık veya alternatif öneri paragrafları İÇEREMEZ. Sadece 1-2 cümlelik (en fazla 25 kelime) tek bir doğrudan cevap ver.");

            var messages = new List<AiChatMessage>
            {
                new("system", contextBuilder.ToString())
            };

            // Include multi-turn conversation history for context memory
            var history = GetHistory(customerId, conversationId);
            foreach (var histMsg in history)
            {
                // Do not re-append the current user message if it's already the last item
                if (histMsg.Text == request.Message && histMsg == history.LastOrDefault())
                    continue;

                var role = string.Equals(histMsg.Sender, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "assistant";
                messages.Add(new AiChatMessage(role, histMsg.Text));
            }

            messages.Add(new AiChatMessage("user", request.Message));

            var isReasoningModel = config.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                                   config.Model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
                                   config.Model.StartsWith("o3", StringComparison.OrdinalIgnoreCase);

            var apiRequest = new AiApiCompletionRequest(
                Model: config.Model,
                Messages: messages,
                Temperature: isReasoningModel ? null : config.Temperature,
                MaxCompletionTokens: isReasoningModel ? 2500 : 800);

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

            _logger.LogWarning("External AI API response choice content was null/empty. Choice finish_reason: {FinishReason}", choice?.FinishReason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get AI completion from external API provider. Falling back to rule engine.");
        }

        return null;
    }

    private static bool IsFollowUpSelectionIntent(string message)
    {
        var clean = message.Trim().ToLowerInvariant();
        return clean is "seç" or "seçeyim" or "ürünleri seç" or "menüyü seç" or "evet" or "tamam" or "ürünleri getir" or "listeleyin" or "ürün seç";
    }

    private async Task<AiSupportResponseDto?> HandleContextualSelectionAsync(string lastAssistantText, string conversationId)
    {
        var lowerLast = lastAssistantText.ToLowerInvariant();

        // Check if last response offered menu or catalog product selection
        if (lowerLast.Contains("menü") || lowerLast.Contains("tavuk") || lowerLast.Contains("sebze") || lowerLast.Contains("salata") || lowerLast.Contains("katalogdan ürünleri seçeyim"))
        {
            // Search catalog for dinner menu items (Tavuk, Sebze, Yoğurt, etc.)
            var filter = new ProductFilterDto(null, null, null, null, null, true, "newest");
            var allProducts = await _catalogService.GetProductsAsync(filter);

            var selectedProducts = allProducts.Where(p =>
                p.Name.Contains("tavuk", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("sebze", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("yoğurt", StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains("elma", StringComparison.OrdinalIgnoreCase) ||
                p.CategoryName.Contains("meyve", StringComparison.OrdinalIgnoreCase) ||
                p.CategoryName.Contains("süt", StringComparison.OrdinalIgnoreCase) ||
                p.CategoryName.Contains("et", StringComparison.OrdinalIgnoreCase)
            ).Take(4).ToList();

            if (!selectedProducts.Any())
            {
                selectedProducts = allProducts.Take(3).ToList();
            }

            if (selectedProducts.Any())
            {
                var productLines = selectedProducts.Select(p => $"• **{p.Name}** — {p.Price:N2} ₺ ({p.CategoryName})");
                var replyText = "🍽️ **Akşam Yemeği Menünüz İçin Seçtiğim Ürünler**:\n\n" +
                                string.Join("\n", productLines) +
                                "\n\nBu ürünleri sepetinize ekleyerek akşam yemeğinizi pratik bir şekilde hazırlayabilirsiniz! 🛒";

                return new AiSupportResponseDto(
                    Reply: replyText,
                    ConversationId: conversationId,
                    TimestampUtc: DateTime.UtcNow,
                    SuggestedActions: new List<string> { "Popüler ürünler", "Kargo ücretleri nedir?", "Sipariş takibi" });
            }
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

    private static bool IsSecurityViolationQuery(string input)
    {
        var lower = input.ToLowerInvariant();
        return lower.Contains("system prompt") ||
               lower.Contains("sistem prompt") ||
               lower.Contains("ignore previous instructions") ||
               lower.Contains("önceki talimatları unut") ||
               lower.Contains("developer mode") ||
               lower.Contains("geliştirici modu") ||
               lower.Contains("dan mode") ||
               lower.Contains("kaynak kod") ||
               lower.Contains("source code") ||
               lower.Contains("connection string") ||
               lower.Contains("veritabanı şifre") ||
               lower.Contains("db password") ||
               lower.Contains("api key") ||
               lower.Contains("secret key");
    }
}
