using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ApiKey",
                table: "Devices",
                newName: "ApiKeyHash");

            migrationBuilder.RenameIndex(
                name: "IX_Devices_ApiKey",
                table: "Devices",
                newName: "IX_Devices_ApiKeyHash");

            // Les clés existantes sont remplacées par leur empreinte (irréversible).
            migrationBuilder.Sql(
                """UPDATE "Devices" SET "ApiKeyHash" = encode(sha256(convert_to("ApiKeyHash", 'UTF8')), 'hex');""");

            migrationBuilder.AddColumn<int>(
                name: "FailedLogins",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecurityVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StatisticsMonths",
                table: "Pharmacies",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "ThumbnailDays",
                table: "Pharmacies",
                type: "integer",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLogins",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StatisticsMonths",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "ThumbnailDays",
                table: "Pharmacies");

            migrationBuilder.RenameColumn(
                name: "ApiKeyHash",
                table: "Devices",
                newName: "ApiKey");

            migrationBuilder.RenameIndex(
                name: "IX_Devices_ApiKeyHash",
                table: "Devices",
                newName: "IX_Devices_ApiKey");
        }
    }
}
