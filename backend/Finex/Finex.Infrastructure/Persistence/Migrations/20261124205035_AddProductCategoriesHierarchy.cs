using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategoriesHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ParentCategoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                    table.CheckConstraint("CK_ProductCategories_NoSelfParent", "[ParentCategoryId] IS NULL OR [ParentCategoryId] <> [Id]");
                    table.CheckConstraint("CK_ProductCategories_ValidLevel", "([ParentCategoryId] IS NULL AND [Level] = 1) OR ([ParentCategoryId] IS NOT NULL AND [Level] >= 2)");
                    table.ForeignKey(
                        name: "FK_ProductCategories_ProductCategories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_ActiveHierarchy",
                table: "ProductCategories",
                columns: new[] { "IsActive", "Level", "ParentCategoryId" },
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_ParentCategoryId",
                table: "ProductCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategories_Code",
                table: "ProductCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategories_ParentId_Name",
                table: "ProductCategories",
                columns: new[] { "ParentCategoryId", "Name" },
                unique: true,
                filter: "[ParentCategoryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ProductCategories_RootName",
                table: "ProductCategories",
                column: "Name",
                unique: true,
                filter: "[ParentCategoryId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductCategories");
        }
    }
}