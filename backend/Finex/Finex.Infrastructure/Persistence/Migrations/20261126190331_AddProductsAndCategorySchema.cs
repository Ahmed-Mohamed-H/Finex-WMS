using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsAndCategorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttributeSchema",
                table: "ProductCategories",
                type: "json",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UnitBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CartonBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    GrossWeightGrams = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    NetWeightGrams = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    VolumeCm3 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "ROUND([LengthCm] * [WidthCm] * [HeightCm], 2)", stored: true),
                    PackagingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PackCapacity = table.Column<int>(type: "int", nullable: false),
                    StorageZone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TechnicalSpecs = table.Column<string>(type: "json", nullable: false),
                    TrackingMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    MaxStackLayers = table.Column<int>(type: "int", nullable: true),
                    MinStockLevel = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Products_DistinctBarcodes", "[UnitBarcode] IS NULL OR [CartonBarcode] IS NULL OR [UnitBarcode] <> [CartonBarcode]");
                    table.CheckConstraint("CK_Products_ValidDimensions", "[LengthCm] > 0 AND [WidthCm] > 0 AND [HeightCm] > 0");
                    table.CheckConstraint("CK_Products_ValidMaxStackLayers", "[MaxStackLayers] IS NULL OR [MaxStackLayers] >= 1");
                    table.CheckConstraint("CK_Products_ValidMinStockLevel", "[MinStockLevel] IS NULL OR [MinStockLevel] >= 0");
                    table.CheckConstraint("CK_Products_ValidPackCapacity", "[PackCapacity] >= 1");
                    table.CheckConstraint("CK_Products_ValidShelfLife", "([TrackingMode] <> 'BatchAndExpiry' AND ([ShelfLifeDays] IS NULL OR [ShelfLifeDays] > 0)) OR ([TrackingMode] = 'BatchAndExpiry' AND [ShelfLifeDays] > 0)");
                    table.CheckConstraint("CK_Products_ValidWeights", "[NetWeightGrams] > 0 AND [GrossWeightGrams] >= [NetWeightGrams]");
                    table.ForeignKey(
                        name: "FK_Products_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_Status",
                table: "Products",
                columns: new[] { "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_Products_CartonBarcode",
                table: "Products",
                column: "CartonBarcode",
                unique: true,
                filter: "[CartonBarcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Products_UnitBarcode",
                table: "Products",
                column: "UnitBarcode",
                unique: true,
                filter: "[UnitBarcode] IS NOT NULL");

            migrationBuilder.Sql("""
                CREATE JSON INDEX IX_Products_TechnicalSpecs_HotPaths
                ON Products (TechnicalSpecs)
                FOR ('$.plyCount', '$.pullCount', '$.foldType', '$.size', '$.phLevel');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Products_TechnicalSpecs_HotPaths ON Products;");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropColumn(
                name: "AttributeSchema",
                table: "ProductCategories");
        }
    }
}