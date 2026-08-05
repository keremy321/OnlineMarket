using System.Text.Json;
using Recommendation.Api.Application.Models;
using Recommendation.Api.Application.Services;
using Recommendation.Api.Domain.Enums;

namespace Recommendation.Api.Tests;

public sealed class RecommendationModelOrchestrationTests
{
    [Fact]
    public void Training_request_is_deterministic_and_contains_no_customer_pii()
    {
        var firstProductId = Guid.Parse(
            "00000000-0000-0000-0000-000000000001");
        var secondProductId = Guid.Parse(
            "00000000-0000-0000-0000-000000000002");
        var firstOrderId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var secondOrderId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");
        var snapshot = new ModelTrainingSnapshot(
            [
                Product(secondProductId, "Second"),
                Product(firstProductId, "First")
            ],
            [
                new ModelOrderInteraction(
                    secondOrderId,
                    Subject('B'),
                    [new ModelOrderInteractionItem(secondProductId, 2)]),
                new ModelOrderInteraction(
                    firstOrderId,
                    Subject('A'),
                    [
                        new ModelOrderInteractionItem(secondProductId, 1),
                        new ModelOrderInteractionItem(firstProductId, 1)
                    ])
            ]);
        var correlationId = Guid.Parse(
            "20000000-0000-0000-0000-000000000001");

        var request = RecommendationModelOrchestrationService.CreateRequest(
            snapshot,
            "tfidf-test-v1",
            correlationId);
        var json = JsonSerializer.Serialize(request);

        Assert.Equal(correlationId, request.CorrelationId);
        Assert.Equal(
            [firstProductId, secondProductId],
            request.Products.Select(product => product.ProductId));
        Assert.Equal(
            [firstOrderId, secondOrderId],
            request.Interactions.Select(interaction => interaction.OrderId));
        Assert.Equal(
            [Subject('A'), Subject('B')],
            request.Interactions.Select(interaction => interaction.SubjectId));
        Assert.Equal(
            [firstProductId, secondProductId],
            request.Interactions[0].Items.Select(item => item.ProductId));
        Assert.DoesNotContain("CustomerId", json, StringComparison.Ordinal);
        Assert.Contains("SubjectId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Address", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone", json, StringComparison.Ordinal);
    }

    private static string Subject(char value)
    {
        return $"v1.{new string(value, 43)}";
    }

    private static ModelProductTrainingSnapshot Product(
        Guid productId,
        string name)
    {
        return new ModelProductTrainingSnapshot(
            productId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            $"{name} description",
            UnitType.Piece,
            1m,
            10m,
            true,
            true);
    }
}
