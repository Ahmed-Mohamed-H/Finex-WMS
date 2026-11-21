using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftProductionReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlantCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    LineCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ShiftCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ShiftEndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ShiftSupervisorCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftProductionReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProducedLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sku = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UnitBarcode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ProductionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ShiftProductionReportId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProducedLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProducedLines_ShiftProductionReports_ShiftProductionReportId",
                        column: x => x.ShiftProductionReportId,
                        principalTable: "ShiftProductionReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProducedLineSsccBarcodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SsccBarcode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ProducedLineId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProducedLineSsccBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProducedLineSsccBarcodes_ProducedLines_ProducedLineId",
                        column: x => x.ProducedLineId,
                        principalTable: "ProducedLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProducedLines_ShiftProductionReportId",
                table: "ProducedLines",
                column: "ShiftProductionReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ProducedLineSsccBarcodes_ProducedLineId",
                table: "ProducedLineSsccBarcodes",
                column: "ProducedLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ProducedLineSsccBarcodes_SsccBarcode",
                table: "ProducedLineSsccBarcodes",
                column: "SsccBarcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftProductionReports_PlantCode_LineCode_ProductionDate_ShiftCode",
                table: "ShiftProductionReports",
                columns: new[] { "PlantCode", "LineCode", "ProductionDate", "ShiftCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProducedLineSsccBarcodes");

            migrationBuilder.DropTable(
                name: "ProducedLines");

            migrationBuilder.DropTable(
                name: "ShiftProductionReports");
        }
    }
}
