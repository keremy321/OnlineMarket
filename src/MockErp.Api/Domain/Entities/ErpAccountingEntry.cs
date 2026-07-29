using MockErp.Api.Domain.Enums;

namespace MockErp.Api.Domain.Entities;

public sealed class ErpAccountingEntry
{
    public Guid Id { get; set; }

    public string ErpVoucherNumber { get; set; } = string.Empty;

    public AccountingVoucherType VoucherType { get; set; } =
        AccountingVoucherType.SalesInvoice;

    public Guid ErpOrderId { get; set; }

    public Guid ExternalOrderId { get; set; }

    public Guid ErpCustomerId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public DateTime EntryDateUtc { get; set; }

    public decimal TotalDebit { get; set; }

    public decimal TotalCredit { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ErpOrder ErpOrder { get; set; } = null!;

    public ErpCustomer ErpCustomer { get; set; } = null!;

    public ICollection<ErpAccountingEntryLine> Lines { get; } = [];
}
