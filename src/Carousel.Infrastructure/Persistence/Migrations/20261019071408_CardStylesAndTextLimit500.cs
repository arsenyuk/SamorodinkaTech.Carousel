using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carousel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CardStylesAndTextLimit500 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE cards SET "Text" = left("Text", 500) WHERE "Text" IS NOT NULL AND length("Text") > 500;""");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "cards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "styles_json",
                table: "cards",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "styles_json",
                table: "cards");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                table: "cards",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
