using System.Security.Cryptography;
using System.Text.Json;
using Recommendation.Api.Contracts;

namespace Recommendation.Api.Application.Services;

public static class RecommendationEventPayloadHasher
{
    public static string Compute(ProductSnapshotChangedV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ComputeHash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(request.EventId), request.EventId);
            writer.WriteString(nameof(request.OccurredAtUtc), request.OccurredAtUtc);
            writer.WriteString(nameof(request.CorrelationId), request.CorrelationId);
            writer.WriteString(nameof(request.ProductId), request.ProductId);
            writer.WriteString(nameof(request.Sku), request.Sku);
            writer.WriteString(nameof(request.Name), request.Name);
            writer.WriteString(nameof(request.CategoryId), request.CategoryId);
            if (request.ParentCategoryId.HasValue)
            {
                writer.WriteString(
                    nameof(request.ParentCategoryId),
                    request.ParentCategoryId.Value);
            }
            else
            {
                writer.WriteNull(nameof(request.ParentCategoryId));
            }

            writer.WriteString(nameof(request.BrandId), request.BrandId);
            writer.WriteNumber(nameof(request.Price), request.Price);
            writer.WriteNumber(nameof(request.NetContent), request.NetContent);
            writer.WriteNumber(nameof(request.UnitType), (byte)request.UnitType);
            writer.WriteBoolean(nameof(request.IsActive), request.IsActive);
            writer.WriteBoolean(nameof(request.IsInStock), request.IsInStock);
            writer.WriteString(
                nameof(request.SourceUpdatedAtUtc),
                request.SourceUpdatedAtUtc);
            writer.WriteEndObject();
        });
    }

    public static string Compute(
        OrderConfirmedForRecommendationV1Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ComputeHash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString(nameof(request.EventId), request.EventId);
            writer.WriteString(nameof(request.OccurredAtUtc), request.OccurredAtUtc);
            writer.WriteString(nameof(request.CorrelationId), request.CorrelationId);
            writer.WriteString(nameof(request.OrderId), request.OrderId);
            writer.WriteString(nameof(request.OrderNumber), request.OrderNumber);
            writer.WriteString(nameof(request.CustomerId), request.CustomerId);
            writer.WriteStartArray(nameof(request.Items));
            foreach (var item in request.Items!)
            {
                writer.WriteStartObject();
                writer.WriteString(nameof(item.ProductId), item!.ProductId);
                writer.WriteNumber(nameof(item.Quantity), item.Quantity);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    private static string ComputeHash(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions { Indented = false }))
        {
            write(writer);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()))
            .ToLowerInvariant();
    }
}
