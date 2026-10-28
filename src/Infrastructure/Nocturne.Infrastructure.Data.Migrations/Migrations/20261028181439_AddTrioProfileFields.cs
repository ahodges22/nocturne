using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nocturne.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrioProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bundle_identifier",
                table: "therapy_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_token",
                table: "therapy_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expiration_date",
                table: "therapy_settings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "override_presets_json",
                table: "therapy_settings",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "team_id",
                table: "therapy_settings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bundle_identifier",
                table: "therapy_settings");

            migrationBuilder.DropColumn(
                name: "device_token",
                table: "therapy_settings");

            migrationBuilder.DropColumn(
                name: "expiration_date",
                table: "therapy_settings");

            migrationBuilder.DropColumn(
                name: "override_presets_json",
                table: "therapy_settings");

            migrationBuilder.DropColumn(
                name: "team_id",
                table: "therapy_settings");
        }
    }
}
