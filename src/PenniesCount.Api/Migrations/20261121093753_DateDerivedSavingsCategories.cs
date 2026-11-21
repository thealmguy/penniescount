using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PenniesCount.Api.Migrations
{
    /// <inheritdoc />
    public partial class DateDerivedSavingsCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Items_Type",
                table: "SavingsItems");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "SavingsItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "SavingsItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Items_Type",
                table: "SavingsItems",
                sql: "[Type] BETWEEN 0 AND 2");
        }
    }
}
