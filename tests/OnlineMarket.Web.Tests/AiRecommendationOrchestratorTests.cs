using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnlineMarket.Web.Application.Interfaces;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Application.Options;
using OnlineMarket.Web.Application.Services;

namespace OnlineMarket.Web.Tests;

/// <summary>
/// Intent-to-endpoint mapping and grounding rules. Every product card must originate
/// from the Recommendation.Api client response.
/// </summary>
public class AiRecommendationOrchestratorTests
{
    // 6. An authenticated general recommendation uses the personalized endpoint.
    [Fact]
    public async Task GeneralRecommendation_AuthenticatedCustomer_UsesPersonalizedEndpoint()
    {
        var customerId = Guid.NewGuid();
        var product = AiAssistantTestData.Product();
        var orchestrator = Create(out var client, out var catalog, out _);
        client.PersonalizedResult = [AiAssistantTestData.Recommendation(product.Id)];
        catalog.Register(product);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.GeneralRecommendation,
            new AiAssistantContext(customerId, null, "Bana ürün öner"));

        Assert.Equal("Personalized", Assert.Single(client.Calls));
        Assert.Equal(customerId, client.LastPersonalizedCustomerId);
        Assert.Equal(AiAssistantIntent.Personalized, outcome.ResolvedIntent);
        Assert.Equal(AiRecommendationStatus.Success, outcome.Status);
    }

    // 7. A guest general recommendation uses the popular endpoint.
    [Fact]
    public async Task GeneralRecommendation_Guest_UsesPopularEndpoint()
    {
        var product = AiAssistantTestData.Product();
        var orchestrator = Create(out var client, out var catalog, out _);
        client.PopularResult = [AiAssistantTestData.Recommendation(product.Id)];
        catalog.Register(product);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.GeneralRecommendation,
            new AiAssistantContext(null, null, "Bana ürün öner"));

        Assert.Equal("Popular", Assert.Single(client.Calls));
        Assert.Equal(AiAssistantIntent.Popular, outcome.ResolvedIntent);
        Assert.Equal(product.Id, Assert.Single(outcome.Products).Id);
    }

    // 8. The popular intent uses the popular endpoint.
    [Fact]
    public async Task PopularIntent_UsesPopularEndpoint()
    {
        var product = AiAssistantTestData.Product();
        var orchestrator = Create(out var client, out var catalog, out _);
        client.PopularResult = [AiAssistantTestData.Recommendation(product.Id)];
        catalog.Register(product);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(Guid.NewGuid(), null, "En popüler ürünler neler?"));

        Assert.Equal("Popular", Assert.Single(client.Calls));
        Assert.Equal(AiAssistantIntent.Popular, outcome.ResolvedIntent);
    }

    // 9. The similar intent uses the similar endpoint with the validated product id.
    [Fact]
    public async Task SimilarIntent_UsesSimilarEndpointWithValidatedProductId()
    {
        var currentProduct = AiAssistantTestData.Product(name: "Süzme Yoğurt");
        var similarProduct = AiAssistantTestData.Product(name: "Kaymaklı Yoğurt");
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(currentProduct, similarProduct);
        client.SimilarResult = [AiAssistantTestData.Recommendation(similarProduct.Id)];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Similar,
            new AiAssistantContext(null, currentProduct.Id, "Buna benzer ürünler göster"));

        Assert.Equal("Similar", Assert.Single(client.Calls));
        Assert.Equal(currentProduct.Id, client.LastSimilarProductId);
        Assert.Equal("Süzme Yoğurt", outcome.ResolvedProductName);
        Assert.Equal(similarProduct.Id, Assert.Single(outcome.Products).Id);
    }

    [Fact]
    public async Task SimilarIntent_UnknownBrowserProductId_IsNotForwardedToRecommendationApi()
    {
        var orchestrator = Create(out var client, out _, out _);

        // The browser claims a product id that does not exist in OnlineMarketDb.
        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Similar,
            new AiAssistantContext(null, Guid.NewGuid(), "Buna benzer ürünler göster"));

        Assert.Empty(client.Calls);
        Assert.Equal(AiRecommendationStatus.NeedsProductClarification, outcome.Status);
    }

    // 10. The frequently-bought-together intent uses the FBT endpoint.
    [Fact]
    public async Task FrequentlyBoughtTogetherIntent_UsesFbtEndpoint()
    {
        var currentProduct = AiAssistantTestData.Product(name: "Tam Buğday Ekmek");
        var companion = AiAssistantTestData.Product(name: "Tereyağı");
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(currentProduct, companion);
        client.FbtResult = [AiAssistantTestData.Recommendation(companion.Id)];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.FrequentlyBoughtTogether,
            new AiAssistantContext(null, currentProduct.Id, "Bunu alanlar başka ne alıyor?"));

        Assert.Equal("Fbt", Assert.Single(client.Calls));
        Assert.Equal(currentProduct.Id, client.LastFbtProductId);
        Assert.Equal(companion.Id, Assert.Single(outcome.Products).Id);
    }

    // 11. Cart completion uses the server-side cart contents.
    [Fact]
    public async Task CartCompletionIntent_UsesServerSideCartContents()
    {
        var customerId = Guid.NewGuid();
        var cartProduct = AiAssistantTestData.Product(name: "Makarna");
        var suggestion = AiAssistantTestData.Product(name: "Domates Sosu");
        var orchestrator = Create(out var client, out var catalog, out var cart);
        catalog.Register(cartProduct, suggestion);
        cart.Items = [StubCartService.CartItem(cartProduct.Id, quantity: 2)];
        client.CartCompletionResult = [AiAssistantTestData.Recommendation(suggestion.Id)];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.CartCompletion,
            new AiAssistantContext(customerId, null, "Sepetimi tamamla"));

        Assert.Equal("CartCompletion", Assert.Single(client.Calls));
        Assert.Equal(customerId, cart.LastRequestedCustomerId);
        Assert.Equal([cartProduct.Id], client.LastCartProductIds);
        Assert.Equal(suggestion.Id, Assert.Single(outcome.Products).Id);
    }

    [Fact]
    public async Task CartCompletionIntent_Guest_AsksForSignIn()
    {
        var orchestrator = Create(out var client, out _, out _);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.CartCompletion,
            new AiAssistantContext(null, null, "Sepetimi tamamla"));

        Assert.Empty(client.Calls);
        Assert.Equal(AiRecommendationStatus.NeedsAuthentication, outcome.Status);
        Assert.Empty(outcome.Products);
    }

    [Fact]
    public async Task CartCompletionIntent_EmptyCart_DoesNotCallRecommendationApi()
    {
        var orchestrator = Create(out var client, out _, out _);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.CartCompletion,
            new AiAssistantContext(Guid.NewGuid(), null, "Sepetimi tamamla"));

        Assert.Empty(client.Calls);
        Assert.Equal(AiRecommendationStatus.EmptyCart, outcome.Status);
    }

    // 12. An ambiguous product request asks for clarification instead of guessing.
    [Fact]
    public async Task SimilarIntent_AmbiguousProduct_AsksForClarification()
    {
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.ProductsToReturn =
        [
            AiAssistantTestData.Product(name: "Yoğurt 500g"),
            AiAssistantTestData.Product(name: "Yoğurt 1kg")
        ];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Similar,
            new AiAssistantContext(null, null, "Yoğurt ürününe benzer ürünler göster"));

        Assert.Empty(client.Calls);
        Assert.Equal(AiRecommendationStatus.NeedsProductClarification, outcome.Status);
        Assert.Empty(outcome.Products);
    }

    [Fact]
    public async Task SimilarIntent_UnknownNamedProduct_AsksForClarification()
    {
        var orchestrator = Create(out var client, out _, out _);

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Similar,
            new AiAssistantContext(null, null, "Brownie ürününe benzer ürünler göster"));

        Assert.Empty(client.Calls);
        Assert.Equal(AiRecommendationStatus.NeedsProductClarification, outcome.Status);
    }

    // 13. An empty engine result produces no invented products.
    [Fact]
    public async Task EmptyRecommendationResult_ProducesNoProducts()
    {
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(AiAssistantTestData.Product());

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(null, null, "En popüler ürünler neler?"));

        Assert.Equal("Popular", Assert.Single(client.Calls));
        Assert.Equal(AiRecommendationStatus.Empty, outcome.Status);
        Assert.Empty(outcome.Products);
    }

    // 15. Structured products come from the recommendation client response only.
    [Fact]
    public async Task Products_AreSourcedFromRecommendationClientResponse()
    {
        var recommended = AiAssistantTestData.Product(name: "Önerilen Ürün");
        var notRecommended = AiAssistantTestData.Product(name: "Önerilmeyen Ürün");
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(recommended, notRecommended);
        client.PopularResult = [AiAssistantTestData.Recommendation(recommended.Id, "Çok satıyor")];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(null, null, "En popüler ürünler neler?"));

        var card = Assert.Single(outcome.Products);
        Assert.Equal(recommended.Id, card.Id);
        Assert.Equal("Önerilen Ürün", card.Name);
        Assert.Equal("Çok satıyor", card.Reason);
        Assert.Equal($"/Catalog/Details/{recommended.Id:D}", card.DetailsUrl);
        Assert.DoesNotContain(outcome.Products, item => item.Id == notRecommended.Id);
    }

    [Theory]
    [InlineData(false, true, 10)]
    [InlineData(true, false, 10)]
    [InlineData(true, true, 0)]
    public async Task Products_FailingCurrentCatalogPolicy_AreDropped(
        bool isActive,
        bool isInStock,
        int stockQuantity)
    {
        var product = AiAssistantTestData.Product(
            isActive: isActive,
            isInStock: isInStock,
            stockQuantity: stockQuantity);
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(product);
        client.PopularResult = [AiAssistantTestData.Recommendation(product.Id)];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(null, null, "En popüler ürünler neler?"));

        Assert.Empty(outcome.Products);
        Assert.Equal(AiRecommendationStatus.Empty, outcome.Status);
    }

    [Fact]
    public async Task Products_WithoutImage_UseTheStorefrontPlaceholder()
    {
        var product = AiAssistantTestData.Product(imageUrl: null);
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(product);
        client.PopularResult = [AiAssistantTestData.Recommendation(product.Id)];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(null, null, "En popüler ürünler neler?"));

        Assert.Equal(
            AiRecommendationOrchestrator.PlaceholderImageUrl,
            Assert.Single(outcome.Products).ImageUrl);
    }

    [Fact]
    public async Task RecommendationApiFailure_ReportsUnavailableWithoutInventingProducts()
    {
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(AiAssistantTestData.Product());
        client.ThrowOnAnyCall = new InvalidOperationException("Recommendation.Api unreachable");

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Popular,
            new AiAssistantContext(null, null, "En popüler ürünler neler?"));

        Assert.Equal(AiRecommendationStatus.Unavailable, outcome.Status);
        Assert.Empty(outcome.Products);
        Assert.Contains(
            "geçici olarak kullanılamıyor",
            AiSupportService.BuildDeterministicReply(outcome),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimilarIntent_ExcludesTheSourceProductFromResults()
    {
        var currentProduct = AiAssistantTestData.Product(name: "Zeytinyağı");
        var alternative = AiAssistantTestData.Product(name: "Ayçiçek Yağı");
        var orchestrator = Create(out var client, out var catalog, out _);
        catalog.Register(currentProduct, alternative);
        client.SimilarResult =
        [
            AiAssistantTestData.Recommendation(currentProduct.Id),
            AiAssistantTestData.Recommendation(alternative.Id)
        ];

        var outcome = await orchestrator.ResolveAsync(
            AiAssistantIntent.Similar,
            new AiAssistantContext(null, currentProduct.Id, "Buna benzer ürünler göster"));

        Assert.Equal(alternative.Id, Assert.Single(outcome.Products).Id);
    }

    [Theory]
    [InlineData("Bana ürün öner", AiAssistantIntent.GeneralRecommendation)]
    [InlineData("Ne almalıyım?", AiAssistantIntent.GeneralRecommendation)]
    [InlineData("Bana uygun ürünler neler?", AiAssistantIntent.GeneralRecommendation)]
    [InlineData("En popüler ürünler neler?", AiAssistantIntent.Popular)]
    [InlineData("En çok tercih edilenleri göster", AiAssistantIntent.Popular)]
    [InlineData("Buna benzer ürünler göster", AiAssistantIntent.Similar)]
    [InlineData("Bu ürünün alternatifi var mı?", AiAssistantIntent.Similar)]
    [InlineData("Bunu alanlar başka ne alıyor?", AiAssistantIntent.FrequentlyBoughtTogether)]
    [InlineData("Bu ürünle beraber ne alınır?", AiAssistantIntent.FrequentlyBoughtTogether)]
    [InlineData("Sepetimi tamamla", AiAssistantIntent.CartCompletion)]
    [InlineData("Sepetime ne eklemeliyim?", AiAssistantIntent.CartCompletion)]
    [InlineData("Bu sepete uygun ürün öner", AiAssistantIntent.CartCompletion)]
    [InlineData("Kargo ücretleri nedir?", AiAssistantIntent.Shipping)]
    [InlineData("Siparişim nerede?", AiAssistantIntent.OrderStatus)]
    [InlineData("İade koşulları neler?", AiAssistantIntent.Returns)]
    [InlineData("Taksit yapıyor musunuz?", AiAssistantIntent.Payment)]
    [InlineData("Merhaba", AiAssistantIntent.Greeting)]
    [InlineData("Hava bugün nasıl?", AiAssistantIntent.Unknown)]
    public void IntentRouter_MapsTurkishMessagesDeterministically(string message, AiAssistantIntent expected)
    {
        Assert.Equal(expected, new AiIntentRouter().Route(message));
    }

    private static AiRecommendationOrchestrator Create(
        out RecordingRecommendationClient recommendationClient,
        out StubCatalogService catalogService,
        out StubCartService cartService)
    {
        recommendationClient = new RecordingRecommendationClient();
        catalogService = new StubCatalogService();
        cartService = new StubCartService();

        return new AiRecommendationOrchestrator(
            recommendationClient,
            catalogService,
            cartService,
            Options.Create(new AiAssistantOptions()),
            NullLogger<AiRecommendationOrchestrator>.Instance);
    }
}
