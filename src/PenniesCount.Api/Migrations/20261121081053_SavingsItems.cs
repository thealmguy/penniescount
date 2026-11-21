using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PenniesCount.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavingsItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavingsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsItems", x => x.Id);
                    table.CheckConstraint("CK_Items_Allocation", "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [TargetAmount]");
                    table.CheckConstraint("CK_Items_Dates", "[TargetDate] >= [StartDate]");
                    table.CheckConstraint("CK_Items_Target", "[TargetAmount] > 0");
                    table.CheckConstraint("CK_Items_Type", "[Type] BETWEEN 0 AND 2");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavingsItems");
        }
    }
}
