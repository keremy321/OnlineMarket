using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recommendation.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationProductDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProductSnapshots",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProductSnapshots");
        }
    }
}
