using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Domain.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string SimulationReference { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Order? Order { get; set; }
}
