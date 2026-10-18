using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carousel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEventSecurityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Login",
                table: "audit_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Result",
                table: "audit_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserIp",
                table: "audit_events",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Login",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "UserIp",
                table: "audit_events");
        }
    }
}
