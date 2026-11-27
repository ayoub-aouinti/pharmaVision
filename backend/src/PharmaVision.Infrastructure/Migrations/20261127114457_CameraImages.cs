using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaVision.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CameraImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CameraImages",
                columns: table => new
                {
                    CameraId = table.Column<int>(type: "integer", nullable: false),
                    Jpeg = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraImages", x => x.CameraId);
                    table.ForeignKey(
                        name: "FK_CameraImages_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CameraImages");
        }
    }
}
