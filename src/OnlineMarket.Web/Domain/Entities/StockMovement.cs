using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Domain.Entities;

public class StockMovement
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public StockMovementType MovementType { get; set; }
    public int QuantityChange { get; set; }
    public int PreviousQuantity { get; set; }
    public int NewQuantity { get; set; }
    public StockReferenceType ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Description { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Product? Product { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }
}
