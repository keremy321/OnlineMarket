using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineMarket.Web.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignOnlineMarketSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "OnlineMarketOrderNumberSequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "OnlineMarketOrderNumberSequence");
        }
    }
}
