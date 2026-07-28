namespace OnlineMarket.Web.Domain.Entities;

public class OrderAddress
{
    public Guid OrderId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string District { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    public Order? Order { get; set; }
}
