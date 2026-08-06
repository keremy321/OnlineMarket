using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;

namespace OnlineMarket.Web.Application.Services;

/// <summary>
/// Assistant orchestration. Product recommendations always come from
/// Recommendation.Api through <see cref="IAiRecommendationOrchestrator"/>; the external
/// provider may only classify an otherwise unknown message and phrase the explanation.
/// The structured product list returned to the browser is built entirely server-side.
/// </summary>
public class AiSupportService : IAiSupportService
{
    private const int MaximumProviderReplyLength = 600;

    /// <summary>
    /// Reasoning models (gpt-5 / o-series) consume completion tokens on internal
    /// reasoning before any text is emitted, so they need a larger budget than the
    /// two-sentence answer itself would suggest.
    /// </summary>
    private const int ReasoningTokenBudget = 3000;

    private readonly IAiIntentRouter _intentRouter;
    private readonly IAiRecommendationOrchestrator _recommendationOrchestrator;
    private readonly IOrderService _orderService;
    private readonly IAiApiClient? _aiApiClient;
    private readonly IOptions<AiAssistantOptions>? _options;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ILogger<AiSupportService> _logger;

    public AiSupportService(
        IAiIntentRouter intentRouter,
        IAiRecommendationOrchestrator recommendationOrchestrator,
        IOrderService orderService,
        IChatHistoryStore chatHistoryStore,
        ILogger<AiSupportService> logger)
        : this(intentRouter, recommendationOrchestrator, orderService, null, null, chatHistoryStore, logger)
    {
    }

    public AiSupportService(
        IAiIntentRouter intentRouter,
        IAiRecommendationOrchestrator recommendationOrchestrator,
        IOrderService orderService,
        IAiApiClient? aiApiClient,
        IOptions<AiAssistantOptions>? options,
        IChatHistoryStore chatHistoryStore,
        ILogger<AiSupportService> logger)
    {
        _intentRouter = intentRouter;
        _recommendationOrchestrator = recommendationOrchestrator;
        _orderService = orderService;
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
            return Deterministic(
                "Lütfen sormak istediğiniz soruyu yazın. Ürün önerileri, sipariş takibi, kargo ve iade konularında yardımcı olabilirim.",
                conversationId,
                AiAssistantIntent.Greeting);
        }

        // OWASP LLM10: bound the work a single message can trigger.
        var maxLength = _options?.Value.EffectiveMaxMessageLength
            ?? AiAssistantOptions.MinimumMessageLength * 32;
        var sanitizedInput = request.Message.Trim();
        if (sanitizedInput.Length > maxLength)
        {
            sanitizedInput = sanitizedInput[..maxLength];
        }

        if (IsSecurityViolationQuery(sanitizedInput))
        {
            var refusal = Deterministic(
                "Güvenlik politikalarımız gereği kaynak kodları, sistem yapılandırması veya teknik detaylar paylaşılamaz. OnlineMarket ürünleri, sipariş, kargo ve ödeme konularında yardımcı olabilirim.",
                conversationId,
                AiAssistantIntent.StoreHelp);

            AddHistoryMessage(customerId, conversationId, "user", sanitizedInput);
            AddHistoryMessage(customerId, conversationId, "assistant", refusal.Reply);
            return refusal;
        }

        AddHistoryMessage(customerId, conversationId, "user", sanitizedInput);

        try
        {
            var intent = _intentRouter.Route(sanitizedInput);
            if (intent == AiAssistantIntent.Unknown)
            {
                // The provider may only help classify; its answer is validated against
                // the approved enum before it can reach an endpoint mapping.
                intent = await TryClassifyWithProviderAsync(sanitizedInput, cancellationToken);
            }

            var response = intent.IsRecommendationIntent()
                ? await HandleRecommendationIntentAsync(
                    intent,
                    request,
                    sanitizedInput,
                    conversationId,
                    customerId,
                    cancellationToken)
                : await HandleStorefrontIntentAsync(
                    intent,
                    sanitizedInput,
                    conversationId,
                    customerId,
                    cancellationToken);

            AddHistoryMessage(customerId, conversationId, "assistant", response.Reply);
            return response;
        }
        catch (OperationCanceledException)
        {
            // A shopper navigating away is not an application error.
            throw;
        }
        catch (Exception exception)
        {
            // Never surface provider or infrastructure detail to the browser.
            _logger.LogError(
                exception,
                "The assistant could not process a chat message for conversation {ConversationId}.",
                conversationId);

            return new AiSupportResponseDto(
                Reply: "Şu anda isteğinizi işlerken geçici bir aksaklık oluştu. Lütfen tekrar deneyin.",
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions(),
                Success: false,
                ErrorMessage: null,
                Intent: nameof(AiAssistantIntent.Unknown),
                UsedFallback: true,
                Products: []);
        }
    }

    private async Task<AiSupportResponseDto> HandleRecommendationIntentAsync(
        AiAssistantIntent intent,
        AiSupportRequestDto request,
        string sanitizedInput,
        string conversationId,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        var context = new AiAssistantContext(
            CustomerId: customerId,
            CurrentProductId: NormalizeProductId(request.CurrentProductId),
            Message: sanitizedInput);

        var outcome = await _recommendationOrchestrator.ResolveAsync(
            intent,
            context,
            cancellationToken);

        var deterministicReply = BuildDeterministicReply(outcome);
        var products = outcome.Products.ToList();

        var reply = deterministicReply;
        var usedFallback = true;

        // Provider prose is only attempted when there is grounded context to describe.
        if (products.Count > 0)
        {
            var prose = await TryExplainWithProviderAsync(
                sanitizedInput,
                outcome,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(prose))
            {
                reply = prose;
                usedFallback = false;
            }
        }

        return new AiSupportResponseDto(
            Reply: reply,
            ConversationId: conversationId,
            TimestampUtc: DateTime.UtcNow,
            SuggestedActions: GetSuggestionsFor(outcome.Status),
            Success: true,
            ErrorMessage: null,
            Intent: outcome.ResolvedIntent.ToString(),
            UsedFallback: usedFallback,
            Products: products);
    }

    private async Task<AiSupportResponseDto> HandleStorefrontIntentAsync(
        AiAssistantIntent intent,
        string sanitizedInput,
        string conversationId,
        Guid? customerId,
        CancellationToken cancellationToken)
    {
        switch (intent)
        {
            case AiAssistantIntent.OrderStatus:
                return await HandleOrderStatusAsync(conversationId, customerId);

            case AiAssistantIntent.Shipping:
                return Deterministic(
                    "500 ₺ ve üzeri siparişlerinizde Ekspres Kargo ücretsizdir; 500 ₺ altı siparişlerde standart kargo ücreti 49,90 ₺'dir.",
                    conversationId,
                    intent);

            case AiAssistantIntent.Payment:
                return Deterministic(
                    "Tüm kredi ve banka kartları ile 256-bit SSL korumalı altyapımız üzerinden güvenle ödeme yapabilirsiniz.",
                    conversationId,
                    intent);

            case AiAssistantIntent.Returns:
                return Deterministic(
                    "Ürünlerinizi teslim aldıktan sonra 14 gün içerisinde Hesabım > Siparişlerim sayfasından iade edebilirsiniz.",
                    conversationId,
                    intent);

            case AiAssistantIntent.Greeting:
                return Deterministic(
                    "Merhaba! OnlineMarket AI Asistanıyım. Ürün önerileri, sipariş takibi, kargo ve iade konularında yardımcı olabilirim.",
                    conversationId,
                    intent);
        }

        // Unknown or general storefront help: the provider may answer, but it has no
        // product context and is instructed never to name a product.
        var prose = await TryExplainWithProviderAsync(
            sanitizedInput,
            new AiRecommendationOutcome(
                AiAssistantIntent.StoreHelp,
                AiRecommendationStatus.Empty,
                []),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(prose))
        {
            return new AiSupportResponseDto(
                Reply: prose,
                ConversationId: conversationId,
                TimestampUtc: DateTime.UtcNow,
                SuggestedActions: GetDefaultSuggestions(),
                Success: true,
                ErrorMessage: null,
                Intent: nameof(AiAssistantIntent.StoreHelp),
                UsedFallback: false,
                Products: []);
        }

        return Deterministic(
            "Size ürün önerileri, sipariş takibi, kargo ve iade konularında yardımcı olabilirim. \"Bana ürün öner\" veya \"En popüler ürünler neler?\" yazabilirsiniz.",
            conversationId,
            AiAssistantIntent.StoreHelp);
    }

    private async Task<AiSupportResponseDto> HandleOrderStatusAsync(
        string conversationId,
        Guid? customerId)
    {
        if (!customerId.HasValue)
        {
            return Deterministic(
                "Sipariş takibi yapabilmem için hesabınıza giriş yapmış olmanız gerekiyor. Giriş yaptıktan sonra son siparişlerinizi buradan kontrol edebilirsiniz.",
                conversationId,
                AiAssistantIntent.OrderStatus);
        }

        var orders = await _orderService.GetCustomerOrdersAsync(customerId.Value);
        if (orders.Count == 0)
        {
            return Deterministic(
                "Henüz verilmiş bir siparişiniz bulunmuyor. Kataloğumuza göz atarak alışverişe başlayabilirsiniz.",
                conversationId,
                AiAssistantIntent.OrderStatus);
        }

        var latestOrder = orders.OrderByDescending(order => order.PlacedAtUtc).First();
        var itemCount = latestOrder.Items.Sum(item => item.Quantity);
        var reply = string.Format(
            new CultureInfo("tr-TR"),
            "Son siparişiniz {0} numaralı sipariş: {1:dd.MM.yyyy HH:mm} tarihinde {2} adet ürün, toplam {3:N2} ₺. Tüm siparişlerinize Hesabım > Siparişlerim sayfasından ulaşabilirsiniz.",
            latestOrder.OrderNumber,
            latestOrder.PlacedAtUtc,
            itemCount,
            latestOrder.GrandTotal);

        return Deterministic(reply, conversationId, AiAssistantIntent.OrderStatus);
    }

    /// <summary>
    /// Deterministic Turkish explanation used whenever the provider is unavailable,
    /// unconfigured or failing. It never names a product; the cards carry that data.
    /// </summary>
    public static string BuildDeterministicReply(AiRecommendationOutcome outcome)
    {
        if (outcome.Status != AiRecommendationStatus.Success)
        {
            return outcome.Status switch
            {
                AiRecommendationStatus.Unavailable =>
                    "Ürün önerileri şu anda geçici olarak kullanılamıyor. Lütfen birazdan tekrar deneyin.",
                AiRecommendationStatus.NeedsProductClarification =>
                    "Hangi ürün için öneri istediğinizi tam adıyla yazar mısınız? Ürün sayfasındayken \"Buna benzer ürünler göster\" diyebilirsiniz.",
                AiRecommendationStatus.NeedsAuthentication =>
                    "Sepetinize göre öneri hazırlayabilmem için hesabınıza giriş yapmanız gerekiyor.",
                AiRecommendationStatus.EmptyCart =>
                    "Sepetiniz şu anda boş. Sepetinize ürün ekledikten sonra tamamlayıcı ürün önerebilirim.",
                _ =>
                    "Şu anda öneri motorumuzdan size uygun bir ürün önerisi gelmedi. Kataloğumuza göz atabilirsiniz."
            };
        }

        var count = outcome.Products.Count;
        return outcome.ResolvedIntent switch
        {
            AiAssistantIntent.Personalized =>
                $"Alışveriş geçmişinize göre öneri motorumuz sizin için {count} ürün seçti.",
            AiAssistantIntent.Popular =>
                $"Şu anda en çok tercih edilen {count} ürünü listeledim.",
            AiAssistantIntent.Similar =>
                string.IsNullOrWhiteSpace(outcome.ResolvedProductName)
                    ? $"Benzer {count} alternatif ürün buldum."
                    : $"{outcome.ResolvedProductName} ürününe benzeyen {count} alternatif buldum.",
            AiAssistantIntent.FrequentlyBoughtTogether =>
                string.IsNullOrWhiteSpace(outcome.ResolvedProductName)
                    ? $"Bu ürünü alanların birlikte tercih ettiği {count} ürün var."
                    : $"{outcome.ResolvedProductName} ürününü alanlar genellikle bu {count} ürünü de alıyor.",
            AiAssistantIntent.CartCompletion =>
                $"Sepetinizi tamamlamak için öneri motorumuzun seçtiği {count} ürün aşağıda.",
            _ =>
                $"Öneri motorumuz sizin için {count} ürün seçti."
        };
    }

    /// <summary>
    /// Asks the provider to phrase the answer. Only the minimum product context is sent:
    /// name, price, category, brand and the engine's reason text. No customer id, subject
    /// id, contact detail, order payload or model score leaves the application.
    /// </summary>
    private async Task<string?> TryExplainWithProviderAsync(
        string sanitizedInput,
        AiRecommendationOutcome outcome,
        CancellationToken cancellationToken)
    {
        var options = _options?.Value;
        if (_aiApiClient is null || options is null || !options.CanCallProvider)
        {
            return null;
        }

        try
        {
            var systemPrompt = new StringBuilder(options.SystemPrompt);
            systemPrompt.AppendLine();
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("RECOMMENDATION_CONTEXT:");
            if (outcome.Products.Count == 0)
            {
                systemPrompt.AppendLine("(boş - hiçbir ürün önerisi yok, ürün adı verme)");
            }
            else
            {
                foreach (var product in outcome.Products)
                {
                    systemPrompt.AppendLine(string.Format(
                        new CultureInfo("tr-TR"),
                        "- {0} | {1:N2} ₺ | Kategori: {2} | Marka: {3} | Gerekçe: {4}",
                        product.Name,
                        product.Price,
                        product.CategoryName,
                        product.BrandName,
                        product.Reason ?? "-"));
                }
            }

            var messages = new List<AiChatMessage>
            {
                new("system", systemPrompt.ToString()),
                new("user", sanitizedInput)
            };

            var isReasoningModel = options.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase)
                || options.Model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
                || options.Model.StartsWith("o3", StringComparison.OrdinalIgnoreCase);

            var apiResponse = await _aiApiClient.GenerateCompletionAsync(
                new AiApiCompletionRequest(
                    Model: options.Model,
                    Messages: messages,
                    Temperature: isReasoningModel ? null : options.Temperature,
                    // Reasoning models spend part of the budget before emitting content.
                    MaxCompletionTokens: isReasoningModel ? ReasoningTokenBudget : 400),
                cancellationToken);

            var choice = apiResponse?.Choices?.FirstOrDefault();
            var sanitized = SanitizeProviderText(choice?.Message?.Content);
            if (sanitized is null)
            {
                _logger.LogWarning(
                    "The assistant provider returned no usable content (finish reason: {FinishReason}); " +
                    "the deterministic explanation will be used.",
                    choice?.FinishReason ?? "none");
            }

            return sanitized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Recommendation.Api results are kept; only the prose is lost.
            _logger.LogWarning(
                exception,
                "The assistant provider call failed; the deterministic explanation will be used.");
            return null;
        }
    }

    /// <summary>
    /// Asks the provider to classify an otherwise unrecognised message. Only a value
    /// inside the approved enum is honoured; anything else stays Unknown.
    /// </summary>
    private async Task<AiAssistantIntent> TryClassifyWithProviderAsync(
        string sanitizedInput,
        CancellationToken cancellationToken)
    {
        var options = _options?.Value;
        if (_aiApiClient is null || options is null || !options.CanCallProvider)
        {
            return AiAssistantIntent.Unknown;
        }

        var approvedValues = string.Join(
            ", ",
            new[]
            {
                nameof(AiAssistantIntent.GeneralRecommendation),
                nameof(AiAssistantIntent.Popular),
                nameof(AiAssistantIntent.Similar),
                nameof(AiAssistantIntent.FrequentlyBoughtTogether),
                nameof(AiAssistantIntent.CartCompletion),
                nameof(AiAssistantIntent.OrderStatus),
                nameof(AiAssistantIntent.Shipping),
                nameof(AiAssistantIntent.Payment),
                nameof(AiAssistantIntent.Returns),
                nameof(AiAssistantIntent.Greeting),
                nameof(AiAssistantIntent.StoreHelp),
                nameof(AiAssistantIntent.Unknown)
            });

        try
        {
            var messages = new List<AiChatMessage>
            {
                new(
                    "system",
                    "Bir e-ticaret mesajını sınıflandır. SADECE şu değerlerden birini tek kelime olarak döndür: " +
                    approvedValues +
                    ". Başka hiçbir metin, açıklama, kod veya URL üretme."),
                new("user", sanitizedInput)
            };

            var apiResponse = await _aiApiClient.GenerateCompletionAsync(
                new AiApiCompletionRequest(
                    Model: options.Model,
                    Messages: messages,
                    Temperature: null,
                    MaxCompletionTokens: ReasoningTokenBudget),
                cancellationToken);

            var content = apiResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return AiAssistantIntent.Unknown;
            }

            var candidate = content
                .Split([' ', '\n', '\r', '\t', '.', ',', ':', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            return AiAssistantIntentExtensions.ParseApproved(candidate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The assistant provider could not classify a message; the deterministic router result is kept.");
            return AiAssistantIntent.Unknown;
        }
    }

    /// <summary>
    /// Strips markup characters and bounds the length so provider output can never carry
    /// HTML into the chat panel.
    /// </summary>
    internal static string? SanitizeProviderText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var builder = new StringBuilder(content.Length);
        foreach (var character in content)
        {
            if (character is '<' or '>')
            {
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().Trim();
        if (sanitized.Length == 0)
        {
            return null;
        }

        return sanitized.Length > MaximumProviderReplyLength
            ? sanitized[..MaximumProviderReplyLength]
            : sanitized;
    }

    private static Guid? NormalizeProductId(Guid? productId) =>
        productId is { } value && value != Guid.Empty ? value : null;

    private static AiSupportResponseDto Deterministic(
        string reply,
        string conversationId,
        AiAssistantIntent intent) =>
        new(
            Reply: reply,
            ConversationId: conversationId,
            TimestampUtc: DateTime.UtcNow,
            SuggestedActions: GetDefaultSuggestions(),
            Success: true,
            ErrorMessage: null,
            Intent: intent.ToString(),
            UsedFallback: true,
            Products: []);

    private void AddHistoryMessage(Guid? accountId, string conversationId, string sender, string text)
    {
        if (accountId.HasValue)
        {
            _chatHistoryStore.AddMessage(accountId.Value, conversationId, sender, text);
        }
    }

    private static List<string> GetSuggestionsFor(AiRecommendationStatus status) =>
        status switch
        {
            AiRecommendationStatus.NeedsProductClarification =>
            [
                "En popüler ürünler neler?",
                "Bana ürün öner"
            ],
            AiRecommendationStatus.NeedsAuthentication or AiRecommendationStatus.EmptyCart =>
            [
                "En popüler ürünler neler?",
                "Kargo ücretleri nedir?"
            ],
            _ => GetDefaultSuggestions()
        };

    private static List<string> GetDefaultSuggestions() =>
    [
        "Bana ürün öner",
        "En popüler ürünler neler?",
        "Kargo ücretleri nedir?",
        "Sipariş takibi"
    ];

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
