using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PenniesCount.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavingsPurposeCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "SavingsItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Items_Category",
                table: "SavingsItems",
                sql: "[Category] IS NULL OR [Category] BETWEEN 0 AND 8");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Items_Category",
                table: "SavingsItems");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "SavingsItems");
        }
    }
}
