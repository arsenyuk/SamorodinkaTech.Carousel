using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carousel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicationNoAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NoAds",
                table: "publications",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoAds",
                table: "publications");
        }
    }
}
