using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PharmaVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SalesImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportId",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PharmacyId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "text", nullable: false),
                    Rows = table.Column<int>(type: "integer", nullable: false),
                    FirstDay = table.Column<DateOnly>(type: "date", nullable: false),
                    LastDay = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesImports_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ImportId",
                table: "Sales",
                column: "ImportId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesImports_PharmacyId",
                table: "SalesImports",
                column: "PharmacyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_SalesImports_ImportId",
                table: "Sales",
                column: "ImportId",
                principalTable: "SalesImports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_SalesImports_ImportId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "SalesImports");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ImportId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ImportId",
                table: "Sales");
        }
    }
}
