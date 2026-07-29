using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recommendation.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRecommendationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerPreferenceScores",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreferenceType = table.Column<byte>(type: "tinyint", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseCount = table.Column<int>(type: "int", nullable: false),
                    TotalQuantity = table.Column<int>(type: "int", nullable: false),
                    LastPurchasedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FrequencyScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    RecencyScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPreferenceScores", x => new { x.CustomerId, x.PreferenceType, x.ReferenceId });
                    table.CheckConstraint("CK_CustomerPreferenceScores_Counts_NonNegative", "[PurchaseCount] >= 0 AND [TotalQuantity] >= 0");
                    table.CheckConstraint("CK_CustomerPreferenceScores_FrequencyScore_Range", "[FrequencyScore] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_CustomerPreferenceScores_RecencyScore_Range", "[RecencyScore] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_CustomerPreferenceScores_Score_Range", "[Score] BETWEEN 0 AND 1");
                });

            migrationBuilder.CreateTable(
                name: "OrderSnapshots",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TotalQuantity = table.Column<int>(type: "int", nullable: false),
                    DistinctProductCount = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSnapshots", x => x.OrderId);
                    table.CheckConstraint("CK_OrderSnapshots_DistinctProductCount_Positive", "[DistinctProductCount] > 0");
                    table.CheckConstraint("CK_OrderSnapshots_TotalQuantity_Positive", "[TotalQuantity] > 0");
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
                name: "ProductSnapshots",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BrandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetContent = table.Column<decimal>(type: "decimal(12,3)", nullable: false),
                    UnitType = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsInStock = table.Column<bool>(type: "bit", nullable: false),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSnapshots", x => x.ProductId);
                    table.CheckConstraint("CK_ProductSnapshots_NetContent_Positive", "[NetContent] > 0");
                    table.CheckConstraint("CK_ProductSnapshots_Price_NonNegative", "[Price] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "RecommendationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunType = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    InputRecordCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OutputRecordCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TriggeredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRuns", x => x.Id);
                    table.CheckConstraint("CK_RecommendationRuns_CompletedAfterStarted", "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [StartedAtUtc]");
                    table.CheckConstraint("CK_RecommendationRuns_Counts_NonNegative", "[InputRecordCount] >= 0 AND [OutputRecordCount] >= 0");
                    table.CheckConstraint("CK_RecommendationRuns_ParametersJson_IsJson", "ISJSON([ParametersJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "OrderSnapshotItems",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSnapshotItems", x => new { x.OrderId, x.ProductId });
                    table.CheckConstraint("CK_OrderSnapshotItems_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_OrderSnapshotItems_OrderSnapshots_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderSnapshots",
                        principalColumn: "OrderId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderSnapshotItems_ProductSnapshots_ProductId",
                        column: x => x.ProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                });

            migrationBuilder.CreateTable(
                name: "ProductAffinities",
                columns: table => new
                {
                    SourceProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecommendedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoOccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    SourceOrderCount = table.Column<int>(type: "int", nullable: false),
                    RecommendedOrderCount = table.Column<int>(type: "int", nullable: false),
                    TotalOrderCount = table.Column<int>(type: "int", nullable: false),
                    Support = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    Lift = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAffinities", x => new { x.SourceProductId, x.RecommendedProductId });
                    table.CheckConstraint("CK_ProductAffinities_Confidence_Range", "[Confidence] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_ProductAffinities_Counts_NonNegative", "[CoOccurrenceCount] >= 0 AND [SourceOrderCount] >= 0 AND [RecommendedOrderCount] >= 0 AND [TotalOrderCount] >= 0");
                    table.CheckConstraint("CK_ProductAffinities_DifferentProducts", "[SourceProductId] <> [RecommendedProductId]");
                    table.CheckConstraint("CK_ProductAffinities_Lift_Positive", "[Lift] > 0");
                    table.CheckConstraint("CK_ProductAffinities_Support_Range", "[Support] BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_ProductAffinities_ProductSnapshots_RecommendedProductId",
                        column: x => x.RecommendedProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductAffinities_ProductSnapshots_SourceProductId",
                        column: x => x.SourceProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductAffinities_RecommendationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "RecommendationRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductPopularity",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    SoldQuantity = table.Column<int>(type: "int", nullable: false),
                    OrderCount = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPopularity", x => x.ProductId);
                    table.CheckConstraint("CK_ProductPopularity_Counts_NonNegative", "[SoldQuantity] >= 0 AND [OrderCount] >= 0");
                    table.CheckConstraint("CK_ProductPopularity_Score_Range", "[Score] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_ProductPopularity_Window", "[WindowStartUtc] < [WindowEndUtc]");
                    table.ForeignKey(
                        name: "FK_ProductPopularity_ProductSnapshots_ProductId",
                        column: x => x.ProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductPopularity_RecommendationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "RecommendationRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductSimilarities",
                columns: table => new
                {
                    SourceProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecommendedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    BrandScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    PriceScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    AmountScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    SimilarityScore = table.Column<decimal>(type: "decimal(12,6)", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSimilarities", x => new { x.SourceProductId, x.RecommendedProductId });
                    table.CheckConstraint("CK_ProductSimilarities_AmountScore_Range", "[AmountScore] BETWEEN 0 AND 0.10");
                    table.CheckConstraint("CK_ProductSimilarities_BrandScore", "[BrandScore] IN (0, 0.10)");
                    table.CheckConstraint("CK_ProductSimilarities_CategoryScore", "[CategoryScore] IN (0, 0.20, 0.65)");
                    table.CheckConstraint("CK_ProductSimilarities_DifferentProducts", "[SourceProductId] <> [RecommendedProductId]");
                    table.CheckConstraint("CK_ProductSimilarities_PriceScore_Range", "[PriceScore] BETWEEN 0 AND 0.15");
                    table.CheckConstraint("CK_ProductSimilarities_Total_EqualsComponents", "[SimilarityScore] = [CategoryScore] + [BrandScore] + [PriceScore] + [AmountScore]");
                    table.CheckConstraint("CK_ProductSimilarities_Total_Range", "[SimilarityScore] BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_ProductSimilarities_ProductSnapshots_RecommendedProductId",
                        column: x => x.RecommendedProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductSimilarities_ProductSnapshots_SourceProductId",
                        column: x => x.SourceProductId,
                        principalTable: "ProductSnapshots",
                        principalColumn: "ProductId");
                    table.ForeignKey(
                        name: "FK_ProductSimilarities_RecommendationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "RecommendationRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPreferenceScores_CustomerId_PreferenceType_Score",
                table: "CustomerPreferenceScores",
                columns: new[] { "CustomerId", "PreferenceType", "Score" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSnapshotItems_ProductId_OrderId",
                table: "OrderSnapshotItems",
                columns: new[] { "ProductId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSnapshots_CustomerId_OccurredAtUtc",
                table: "OrderSnapshots",
                columns: new[] { "CustomerId", "OccurredAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSnapshots_OccurredAtUtc",
                table: "OrderSnapshots",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "UX_OrderSnapshots_CorrelationId",
                table: "OrderSnapshots",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_OrderSnapshots_OrderNumber",
                table: "OrderSnapshots",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_CorrelationId",
                table: "ProcessedEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEvents_EventType_ProcessedAtUtc",
                table: "ProcessedEvents",
                columns: new[] { "EventType", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAffinities_SourceProductId_Score",
                table: "ProductAffinities",
                columns: new[] { "SourceProductId", "Score" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "RecommendedProductId", "Confidence", "Lift" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPopularity_Score",
                table: "ProductPopularity",
                column: "Score",
                descending: new bool[0])
                .Annotation("SqlServer:Include", new[] { "ProductId", "SoldQuantity", "OrderCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSimilarities_SourceProductId_SimilarityScore",
                table: "ProductSimilarities",
                columns: new[] { "SourceProductId", "SimilarityScore" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSnapshots_BrandId_IsActive_IsInStock",
                table: "ProductSnapshots",
                columns: new[] { "BrandId", "IsActive", "IsInStock" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSnapshots_CategoryId_IsActive_IsInStock",
                table: "ProductSnapshots",
                columns: new[] { "CategoryId", "IsActive", "IsInStock" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductSnapshots_Sku",
                table: "ProductSnapshots",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_CorrelationId",
                table: "RecommendationRuns",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_RunType_StartedAtUtc",
                table: "RecommendationRuns",
                columns: new[] { "RunType", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_Status_StartedAtUtc",
                table: "RecommendationRuns",
                columns: new[] { "Status", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerPreferenceScores");

            migrationBuilder.DropTable(
                name: "OrderSnapshotItems");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropTable(
                name: "ProductAffinities");

            migrationBuilder.DropTable(
                name: "ProductPopularity");

            migrationBuilder.DropTable(
                name: "ProductSimilarities");

            migrationBuilder.DropTable(
                name: "OrderSnapshots");

            migrationBuilder.DropTable(
                name: "ProductSnapshots");

            migrationBuilder.DropTable(
                name: "RecommendationRuns");
        }
    }
}
