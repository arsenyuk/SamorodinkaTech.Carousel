using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carousel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarouselTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "carousel_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultStylesJson = table.Column<string>(type: "jsonb", nullable: true),
                    BackgroundMediaAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carousel_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_carousel_templates_media_assets_BackgroundMediaAssetId",
                        column: x => x.BackgroundMediaAssetId,
                        principalTable: "media_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publications_TemplateId",
                table: "publications",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_carousel_templates_BackgroundMediaAssetId",
                table: "carousel_templates",
                column: "BackgroundMediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_carousel_templates_Code",
                table: "carousel_templates",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_publications_carousel_templates_TemplateId",
                table: "publications",
                column: "TemplateId",
                principalTable: "carousel_templates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_publications_carousel_templates_TemplateId",
                table: "publications");

            migrationBuilder.DropTable(
                name: "carousel_templates");

            migrationBuilder.DropIndex(
                name: "IX_publications_TemplateId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "publications");
        }
    }
}
