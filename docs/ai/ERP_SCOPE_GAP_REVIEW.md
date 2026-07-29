# ERP Scope Gap Review

## Source reviewed

`erp.docx` compares the project model with common ERP screens:

- customer/current-account card,
- order/invoice,
- stock card and movements,
- accounting voucher.

## Findings corrected in the AI documents

### 1. Payment method was not guaranteed in the ERP event

Decision:

- add `PaymentMethod` to `OrderReadyForErpV1`,
- persist it in `IntegrationOrderSnapshots`,
- persist it in `ErpOrders`,
- snapshot it on the accounting voucher.

This value is non-sensitive. Card number, CVV, expiry date, provider token,
and provider credentials remain forbidden.

### 2. Delivery address reached IntegrationDb but was not guaranteed in MockErpDb

Decision:

- add one-to-one `ErpOrderAddresses`,
- persist the selected delivery address atomically with ERP order creation,
- keep it immutable for historical order accuracy.

### 3. ERP stock card lacked unit/content and critical-stock threshold

Decision:

- add `UnitType`, `NetContent`, and `ReorderLevel` to `ErpStocks`,
- keep ERP stock independent from market stock,
- keep one-warehouse assumption; do not add warehouse/location tables.

The market database already defines `Stocks.ReorderLevel`.

### 4. Accounting voucher was too shallow

The previous model had one header with total debit/credit but no account-coded
lines and no direct customer reference.

Decision:

- keep an accounting header,
- add direct `ErpCustomerId`,
- add `VoucherType`, `EntryDateUtc`, `PaymentMethod`, and required description,
- add `ErpAccountingEntryLines`,
- create deterministic V1 lines:

```text
120 Customers/Receivables  Debit  = GrandTotal
600 Domestic Sales        Credit = Subtotal
391 VAT Payable           Credit = VatTotal
```

- require total debit to equal total credit,
- persist header, lines, and idempotency record atomically.

This is a project simulation, not a legal accounting design.

## Findings deliberately kept out of scope

The following ERP fields remain excluded because they are unnecessary for the
approved project flow or would expand privacy/compliance scope:

- tax number / Turkish identity number,
- open-account balance and maturity tracking,
- supplier/current-account type,
- draft/approval/cancellation document workflow,
- dispatch note and shipment operations,
- multi-warehouse/location management,
- legal invoice, e-ledger, or e-invoice compliance.

## Compatibility impact

These changes affect:

- `OrderReadyForErpV1` producer and consumer DTOs,
- canonical payload hashing,
- ERP Integration validation and snapshot persistence,
- Mock ERP order and accounting contracts,
- IntegrationDb and MockErpDb migrations,
- serialization, idempotency, migration, and SQL Server integration tests.

Because `PaymentMethod` becomes required, existing pre-release V1 producer and
consumer implementations must be updated together before merge.
