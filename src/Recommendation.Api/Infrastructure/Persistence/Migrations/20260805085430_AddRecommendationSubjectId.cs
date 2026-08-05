using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recommendation.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationSubjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubjectId",
                table: "OrderSnapshots",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderSnapshots_SubjectId_OccurredAtUtc",
                table: "OrderSnapshots",
                columns: new[] { "SubjectId", "OccurredAtUtc" },
                descending: new[] { false, true },
                filter: "[SubjectId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderSnapshots_SubjectId_OccurredAtUtc",
                table: "OrderSnapshots");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "OrderSnapshots");
        }
    }
}
