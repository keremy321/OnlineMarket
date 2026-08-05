using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Common.Messaging;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTime OccurredAtUtc { get; }
    Guid CorrelationId { get; }
}

public sealed record ProductSnapshotChangedV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid CorrelationId,
    Guid ProductId,
    string Sku,
    string Name,
    string? Description,
    Guid CategoryId,
    Guid? ParentCategoryId,
    Guid BrandId,
    decimal Price,
    decimal NetContent,
    UnitType UnitType,
    bool IsActive,
    bool IsInStock,
    DateTime SourceUpdatedAtUtc) : IIntegrationEvent;

public sealed record RecommendationOrderItemV1(
    Guid ProductId,
    int Quantity);

public sealed record OrderConfirmedForRecommendationV1(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid CorrelationId,
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    IReadOnlyList<RecommendationOrderItemV1> Items) : IIntegrationEvent;

public sealed record ErpOrderCustomerV1(
    Guid CustomerId,
    string FirstName,
    string LastName,
    string Email);

public sealed record ErpOrderAddressV1(
    string RecipientName,
    string PhoneNumber,
    string AddressLine1,
    string? AddressLine2,
    string District,
    string City,
    string? PostalCode,
    string CountryCode);

public sealed record ErpOrderTotalsV1(
    decimal Subtotal,
    decimal VatTotal,
    decimal GrandTotal,
    string Currency);

public sealed record ErpOrderItemV1(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal NetLineAmount,
    decimal VatAmount,
    decimal LineTotal);

public sealed record OrderReadyForErpV1 : IIntegrationEvent
{
    public OrderReadyForErpV1(
        Guid eventId,
        DateTime occurredAtUtc,
        Guid correlationId,
        Guid orderId,
        string orderNumber,
        DateTime orderPlacedAtUtc,
        PaymentMethod paymentMethod,
        ErpOrderCustomerV1 customer,
        ErpOrderAddressV1 address,
        ErpOrderTotalsV1 totals,
        IReadOnlyList<ErpOrderItemV1> items)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count)
        {
            throw new ArgumentException("ERP order event items must have unique ProductId values.", nameof(items));
        }

        EventId = eventId;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        OrderId = orderId;
        OrderNumber = orderNumber;
        OrderPlacedAtUtc = orderPlacedAtUtc;
        PaymentMethod = paymentMethod;
        Customer = customer;
        Address = address;
        Totals = totals;
        Items = items.ToArray();
    }

    public Guid EventId { get; }
    public DateTime OccurredAtUtc { get; }
    public Guid CorrelationId { get; }
    public Guid OrderId { get; }
    public string OrderNumber { get; }
    public DateTime OrderPlacedAtUtc { get; }
    public PaymentMethod PaymentMethod { get; }
    public ErpOrderCustomerV1 Customer { get; }
    public ErpOrderAddressV1 Address { get; }
    public ErpOrderTotalsV1 Totals { get; }
    public IReadOnlyList<ErpOrderItemV1> Items { get; }
}
