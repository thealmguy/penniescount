using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PenniesCount.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavingsDrawdowns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRemoved",
                table: "SavingsItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Drawdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavingsItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drawdowns", x => x.Id);
                    table.CheckConstraint("CK_Drawdowns_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Drawdowns_SavingsItems_SavingsItemId",
                        column: x => x.SavingsItemId,
                        principalTable: "SavingsItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Drawdowns_SavingsItemId_SpentOn_RecordedAtUtc_Id",
                table: "Drawdowns",
                columns: new[] { "SavingsItemId", "SpentOn", "RecordedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Drawdowns");

            migrationBuilder.DropColumn(
                name: "IsRemoved",
                table: "SavingsItems");
        }
    }
}
