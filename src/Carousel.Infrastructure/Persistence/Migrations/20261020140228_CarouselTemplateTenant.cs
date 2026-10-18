using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carousel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarouselTemplateTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_carousel_templates_Code",
                table: "carousel_templates");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "carousel_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_carousel_templates_Code",
                table: "carousel_templates",
                column: "Code",
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_carousel_templates_TenantId_Code",
                table: "carousel_templates",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_carousel_templates_tenants_TenantId",
                table: "carousel_templates",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_carousel_templates_tenants_TenantId",
                table: "carousel_templates");

            migrationBuilder.DropIndex(
                name: "IX_carousel_templates_Code",
                table: "carousel_templates");

            migrationBuilder.DropIndex(
                name: "IX_carousel_templates_TenantId_Code",
                table: "carousel_templates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "carousel_templates");

            migrationBuilder.CreateIndex(
                name: "IX_carousel_templates_Code",
                table: "carousel_templates",
                column: "Code",
                unique: true);
        }
    }
}
