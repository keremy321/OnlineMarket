using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MockErp.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMockErpSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErpCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpCustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErpIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ResponseStatusCode = table.Column<short>(type: "smallint", nullable: false),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpIdempotencyRecords", x => x.Id);
                    table.CheckConstraint("CK_ErpIdempotencyRecords_ResponseBody_IsJson", "ISJSON([ResponseBody]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "ErpStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitType = table.Column<byte>(type: "tinyint", nullable: false),
                    NetContent = table.Column<decimal>(type: "decimal(12,3)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpStocks", x => x.Id);
                    table.CheckConstraint("CK_ErpStocks_NetContent_Positive", "[NetContent] > 0");
                    table.CheckConstraint("CK_ErpStocks_Quantity_NonNegative", "[Quantity] >= 0");
                    table.CheckConstraint("CK_ErpStocks_ReorderLevel_NonNegative", "[ReorderLevel] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ErpOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpOrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketOrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErpCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderPlacedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PaymentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpOrders", x => x.Id);
                    table.CheckConstraint("CK_ErpOrders_GrandTotal", "[GrandTotal] = [Subtotal] + [VatTotal]");
                    table.CheckConstraint("CK_ErpOrders_Totals_NonNegative", "[Subtotal] >= 0 AND [VatTotal] >= 0 AND [GrandTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_ErpOrders_ErpCustomers_ErpCustomerId",
                        column: x => x.ErpCustomerId,
                        principalTable: "ErpCustomers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ErpAccountingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpVoucherNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VoucherType = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),
                    ErpOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    EntryDateUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TotalDebit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpAccountingEntries", x => x.Id);
                    table.CheckConstraint("CK_ErpAccountingEntries_Amounts_NonNegative", "[TotalDebit] >= 0 AND [TotalCredit] >= 0");
                    table.CheckConstraint("CK_ErpAccountingEntries_Balanced", "[TotalDebit] = [TotalCredit]");
                    table.ForeignKey(
                        name: "FK_ErpAccountingEntries_ErpCustomers_ErpCustomerId",
                        column: x => x.ErpCustomerId,
                        principalTable: "ErpCustomers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ErpAccountingEntries_ErpOrders_ErpOrderId",
                        column: x => x.ErpOrderId,
                        principalTable: "ErpOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ErpOrderAddresses",
                columns: table => new
                {
                    ErpOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false, defaultValue: "TR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpOrderAddresses", x => x.ErpOrderId);
                    table.ForeignKey(
                        name: "FK_ErpOrderAddresses_ErpOrders_ErpOrderId",
                        column: x => x.ErpOrderId,
                        principalTable: "ErpOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ErpOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    NetLineAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpOrderLines", x => x.Id);
                    table.CheckConstraint("CK_ErpOrderLines_Amounts_NonNegative", "[UnitPrice] >= 0 AND [NetLineAmount] >= 0 AND [VatAmount] >= 0 AND [LineTotal] >= 0");
                    table.CheckConstraint("CK_ErpOrderLines_LineTotal", "[LineTotal] = [NetLineAmount] + [VatAmount]");
                    table.CheckConstraint("CK_ErpOrderLines_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_ErpOrderLines_VatRate_Range", "[VatRate] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ErpOrderLines_ErpOrders_ErpOrderId",
                        column: x => x.ErpOrderId,
                        principalTable: "ErpOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ErpStockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    QuantityChange = table.Column<int>(type: "int", nullable: false),
                    PreviousQuantity = table.Column<int>(type: "int", nullable: false),
                    NewQuantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpStockMovements", x => x.Id);
                    table.CheckConstraint("CK_ErpStockMovements_Balance", "[PreviousQuantity] + [QuantityChange] = [NewQuantity]");
                    table.CheckConstraint("CK_ErpStockMovements_Quantities_NonNegative", "[PreviousQuantity] >= 0 AND [NewQuantity] >= 0");
                    table.CheckConstraint("CK_ErpStockMovements_QuantityChange_NotZero", "[QuantityChange] <> 0");
                    table.ForeignKey(
                        name: "FK_ErpStockMovements_ErpOrders_ErpOrderId",
                        column: x => x.ErpOrderId,
                        principalTable: "ErpOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ErpAccountingEntryLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountingEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<byte>(type: "tinyint", nullable: false),
                    AccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ErpCustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DebitAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpAccountingEntryLines", x => x.Id);
                    table.CheckConstraint("CK_ErpAccountingEntryLines_AccountCode_V1", "[AccountCode] IN ('120','600','391')");
                    table.CheckConstraint("CK_ErpAccountingEntryLines_Amounts_NonNegative", "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
                    table.CheckConstraint("CK_ErpAccountingEntryLines_OneSided", "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([DebitAmount] = 0 AND [CreditAmount] > 0)");
                    table.CheckConstraint("CK_ErpAccountingEntryLines_Sequence_Range", "[SequenceNumber] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ErpAccountingEntryLines_ErpAccountingEntries_AccountingEntryId",
                        column: x => x.AccountingEntryId,
                        principalTable: "ErpAccountingEntries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ErpAccountingEntryLines_ErpCustomers_ErpCustomerId",
                        column: x => x.ErpCustomerId,
                        principalTable: "ErpCustomers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErpAccountingEntries_ErpCustomerId_EntryDateUtc",
                table: "ErpAccountingEntries",
                columns: new[] { "ErpCustomerId", "EntryDateUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ErpAccountingEntries_ErpOrderId",
                table: "ErpAccountingEntries",
                column: "ErpOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpAccountingEntries_ErpVoucherNumber",
                table: "ErpAccountingEntries",
                column: "ErpVoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpAccountingEntries_ExternalOrderId",
                table: "ErpAccountingEntries",
                column: "ExternalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpAccountingEntryLines_AccountCode_CreatedAtUtc",
                table: "ErpAccountingEntryLines",
                columns: new[] { "AccountCode", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ErpAccountingEntryLines_ErpCustomerId_CreatedAtUtc",
                table: "ErpAccountingEntryLines",
                columns: new[] { "ErpCustomerId", "CreatedAtUtc" },
                filter: "[ErpCustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ErpAccountingEntryLines_Entry_Account",
                table: "ErpAccountingEntryLines",
                columns: new[] { "AccountingEntryId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpAccountingEntryLines_Entry_Sequence",
                table: "ErpAccountingEntryLines",
                columns: new[] { "AccountingEntryId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpCustomers_Email",
                table: "ErpCustomers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "UX_ErpCustomers_ErpCustomerCode",
                table: "ErpCustomers",
                column: "ErpCustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpCustomers_ExternalCustomerId",
                table: "ErpCustomers",
                column: "ExternalCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpIdempotencyRecords_OperationType_CreatedAtUtc",
                table: "ErpIdempotencyRecords",
                columns: new[] { "OperationType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ErpIdempotencyRecords_ResourceType_ResourceId",
                table: "ErpIdempotencyRecords",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "UX_ErpIdempotencyRecords_IdempotencyKey",
                table: "ErpIdempotencyRecords",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpOrderLines_ExternalProductId",
                table: "ErpOrderLines",
                column: "ExternalProductId");

            migrationBuilder.CreateIndex(
                name: "UX_ErpOrderLines_ErpOrderId_ExternalProductId",
                table: "ErpOrderLines",
                columns: new[] { "ErpOrderId", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpOrders_ErpCustomerId_CreatedAtUtc",
                table: "ErpOrders",
                columns: new[] { "ErpCustomerId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_ErpOrders_ErpOrderNumber",
                table: "ErpOrders",
                column: "ErpOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpOrders_ExternalOrderId",
                table: "ErpOrders",
                column: "ExternalOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpOrders_MarketOrderNumber",
                table: "ErpOrders",
                column: "MarketOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpStockMovements_ErpOrderId",
                table: "ErpStockMovements",
                column: "ErpOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpStockMovements_ExternalProductId_CreatedAtUtc",
                table: "ErpStockMovements",
                columns: new[] { "ExternalProductId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ErpStockMovements_ExternalOrderId_ExternalProductId",
                table: "ErpStockMovements",
                columns: new[] { "ExternalOrderId", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ErpStocks_Quantity",
                table: "ErpStocks",
                column: "Quantity");

            migrationBuilder.CreateIndex(
                name: "UX_ErpStocks_ExternalProductId",
                table: "ErpStocks",
                column: "ExternalProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpStocks_Sku",
                table: "ErpStocks",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErpAccountingEntryLines");

            migrationBuilder.DropTable(
                name: "ErpIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "ErpOrderAddresses");

            migrationBuilder.DropTable(
                name: "ErpOrderLines");

            migrationBuilder.DropTable(
                name: "ErpStockMovements");

            migrationBuilder.DropTable(
                name: "ErpStocks");

            migrationBuilder.DropTable(
                name: "ErpAccountingEntries");

            migrationBuilder.DropTable(
                name: "ErpOrders");

            migrationBuilder.DropTable(
                name: "ErpCustomers");
        }
    }
}
