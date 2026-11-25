using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftReportReviewLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShiftProductionReports_PlantCode_LineCode_ProductionDate_ShiftCode",
                table: "ShiftProductionReports");

            migrationBuilder.DropIndex(
                name: "IX_ProducedLineSsccBarcodes_SsccBarcode",
                table: "ProducedLineSsccBarcodes");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "ShiftProductionReports",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAtUtc",
                table: "ShiftProductionReports",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "ShiftProductionReports",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "ShiftProductionReports",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "ShiftProductionReports",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SupersededByReportId",
                table: "ShiftProductionReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProducedLineSsccBarcodes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftProductionReports_SupersededByReportId",
                table: "ShiftProductionReports",
                column: "SupersededByReportId");

            migrationBuilder.CreateIndex(
                name: "UX_ShiftProductionReports_ActiveShift",
                table: "ShiftProductionReports",
                columns: new[] { "PlantCode", "LineCode", "ProductionDate", "ShiftCode" },
                unique: true,
                filter: "[Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "UX_ProducedLineSsccBarcodes_ActiveSscc",
                table: "ProducedLineSsccBarcodes",
                column: "SsccBarcode",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_ShiftProductionReports_ShiftProductionReports_SupersededByReportId",
                table: "ShiftProductionReports",
                column: "SupersededByReportId",
                principalTable: "ShiftProductionReports",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShiftProductionReports_ShiftProductionReports_SupersededByReportId",
                table: "ShiftProductionReports");

            migrationBuilder.DropIndex(
                name: "IX_ShiftProductionReports_SupersededByReportId",
                table: "ShiftProductionReports");

            migrationBuilder.DropIndex(
                name: "UX_ShiftProductionReports_ActiveShift",
                table: "ShiftProductionReports");

            migrationBuilder.DropIndex(
                name: "UX_ProducedLineSsccBarcodes_ActiveSscc",
                table: "ProducedLineSsccBarcodes");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "SupersededByReportId",
                table: "ShiftProductionReports");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProducedLineSsccBarcodes");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftProductionReports_PlantCode_LineCode_ProductionDate_ShiftCode",
                table: "ShiftProductionReports",
                columns: new[] { "PlantCode", "LineCode", "ProductionDate", "ShiftCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProducedLineSsccBarcodes_SsccBarcode",
                table: "ProducedLineSsccBarcodes",
                column: "SsccBarcode",
                unique: true);
        }
    }
}