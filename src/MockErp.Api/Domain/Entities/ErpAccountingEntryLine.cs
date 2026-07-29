namespace MockErp.Api.Domain.Entities;

public sealed class ErpAccountingEntryLine
{
    public Guid Id { get; set; }

    public Guid AccountingEntryId { get; set; }

    public byte SequenceNumber { get; set; }

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    public Guid? ErpCustomerId { get; set; }

    public decimal DebitAmount { get; set; }

    public decimal CreditAmount { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ErpAccountingEntry AccountingEntry { get; set; } = null!;

    public ErpCustomer? ErpCustomer { get; set; }
}
