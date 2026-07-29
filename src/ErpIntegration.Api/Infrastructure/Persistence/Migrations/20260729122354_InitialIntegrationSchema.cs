using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpIntegration.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIntegrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErpCustomerLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErpCustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpCustomerLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CurrentStepType = table.Column<byte>(type: "tinyint", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationBatches_ProcessedEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "ProcessedEvents",
                        principalColumn: "EventId");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_IntegrationOrderLines", x => x.Id);
                    table.CheckConstraint("CK_IntegrationOrderLines_Amounts_NonNegative", "[UnitPrice] >= 0 AND [NetLineAmount] >= 0 AND [VatAmount] >= 0 AND [LineTotal] >= 0");
                    table.CheckConstraint("CK_IntegrationOrderLines_LineTotal", "[LineTotal] = [NetLineAmount] + [VatAmount]");
                    table.CheckConstraint("CK_IntegrationOrderLines_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_IntegrationOrderLines_VatRate_Range", "[VatRate] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_IntegrationOrderLines_IntegrationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "IntegrationBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOrderSnapshots",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AddressLine1 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    AddressLine2 = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    District = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CountryCode = table.Column<string>(type: "char(2)", unicode: false, fixedLength: true, maxLength: 2, nullable: false),
                    OrderPlacedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    PaymentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationOrderSnapshots", x => x.BatchId);
                    table.CheckConstraint("CK_IntegrationOrderSnapshots_GrandTotal", "[GrandTotal] = [Subtotal] + [VatTotal]");
                    table.CheckConstraint("CK_IntegrationOrderSnapshots_Totals_NonNegative", "[Subtotal] >= 0 AND [VatTotal] >= 0 AND [GrandTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_IntegrationOrderSnapshots_IntegrationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "IntegrationBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepType = table.Column<byte>(type: "tinyint", nullable: false),
                    SequenceNumber = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 5),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastHttpStatusCode = table.Column<short>(type: "smallint", nullable: true),
                    LastErrorType = table.Column<byte>(type: "tinyint", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSteps", x => x.Id);
                    table.CheckConstraint("CK_IntegrationSteps_Attempts_Range", "[AttemptCount] >= 0 AND [MaxAttempts] > 0 AND [AttemptCount] <= [MaxAttempts]");
                    table.CheckConstraint("CK_IntegrationSteps_Sequence_Range", "[SequenceNumber] BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_IntegrationSteps_IntegrationBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "IntegrationBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    ResultType = table.Column<byte>(type: "tinyint", nullable: false),
                    HttpStatusCode = table.Column<short>(type: "smallint", nullable: true),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RequestPayloadMasked = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponsePayloadMasked = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationAttempts", x => x.Id);
                    table.CheckConstraint("CK_IntegrationAttempts_AttemptNumber_Positive", "[AttemptNumber] > 0");
                    table.CheckConstraint("CK_IntegrationAttempts_Duration_NonNegative", "[DurationMs] IS NULL OR [DurationMs] >= 0");
                    table.CheckConstraint("CK_IntegrationAttempts_RequestPayloadMasked_IsJson", "[RequestPayloadMasked] IS NULL OR ISJSON([RequestPayloadMasked]) = 1");
                    table.CheckConstraint("CK_IntegrationAttempts_ResponsePayloadMasked_IsJson", "[ResponsePayloadMasked] IS NULL OR ISJSON([ResponsePayloadMasked]) = 1");
                    table.ForeignKey(
                        name: "FK_IntegrationAttempts_IntegrationSteps_StepId",
                        column: x => x.StepId,
                        principalTable: "IntegrationSteps",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_ErpCustomerLinks_CustomerId",
                table: "ErpCustomerLinks",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ErpCustomerLinks_ErpCustomerCode",
                table: "ErpCustomerLinks",
                column: "ErpCustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationAttempts_CorrelationId",
                table: "IntegrationAttempts",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationAttempts_StepId_StartedAtUtc",
                table: "IntegrationAttempts",
                columns: new[] { "StepId", "StartedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationAttempts_StepId_AttemptNumber",
                table: "IntegrationAttempts",
                columns: new[] { "StepId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationBatches_CustomerId_CreatedAtUtc",
                table: "IntegrationBatches",
                columns: new[] { "CustomerId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationBatches_Status_CreatedAtUtc",
                table: "IntegrationBatches",
                columns: new[] { "Status", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationBatches_CorrelationId",
                table: "IntegrationBatches",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationBatches_EventId",
                table: "IntegrationBatches",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationBatches_MarketOrderId",
                table: "IntegrationBatches",
                column: "MarketOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationBatches_OrderNumber",
                table: "IntegrationBatches",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationOrderLines_BatchId_ProductId",
                table: "IntegrationOrderLines",
                columns: new[] { "BatchId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSteps_Status_NextAttemptAtUtc_SequenceNumber",
                table: "IntegrationSteps",
                columns: new[] { "Status", "NextAttemptAtUtc", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationSteps_BatchId_SequenceNumber",
                table: "IntegrationSteps",
                columns: new[] { "BatchId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationSteps_BatchId_StepType",
                table: "IntegrationSteps",
                columns: new[] { "BatchId", "StepType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationSteps_IdempotencyKey",
                table: "IntegrationSteps",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_CorrelationId",
                table: "ProcessedEvents",
                column: "CorrelationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErpCustomerLinks");

            migrationBuilder.DropTable(
                name: "IntegrationAttempts");

            migrationBuilder.DropTable(
                name: "IntegrationOrderLines");

            migrationBuilder.DropTable(
                name: "IntegrationOrderSnapshots");

            migrationBuilder.DropTable(
                name: "IntegrationSteps");

            migrationBuilder.DropTable(
                name: "IntegrationBatches");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");
        }
    }
}
