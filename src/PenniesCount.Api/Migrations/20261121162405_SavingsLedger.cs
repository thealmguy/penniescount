using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PenniesCount.Api.Migrations
{
    /// <inheritdoc />
    public partial class SavingsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SavingsItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsInferred = table.Column<bool>(type: "bit", nullable: false),
                    ReversesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_Ledger_Amount", "([Type] IN (2, 4) AND [Amount] <> 0) OR ([Type] NOT IN (2, 4) AND [Amount] > 0)");
                    table.CheckConstraint("CK_Ledger_Replacement", "[ReplacesEntryId] IS NULL OR [Type] = 3");
                    table.CheckConstraint("CK_Ledger_Reversal", "([Type] = 4 AND [ReversesEntryId] IS NOT NULL) OR ([Type] <> 4 AND [ReversesEntryId] IS NULL)");
                    table.CheckConstraint("CK_Ledger_Type", "[Type] BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerEntries_ReplacesEntryId",
                        column: x => x.ReplacesEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_SavingsItems_SavingsItemId",
                        column: x => x.SavingsItemId,
                        principalTable: "SavingsItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReplacesEntryId",
                table: "LedgerEntries",
                column: "ReplacesEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReversesEntryId",
                table: "LedgerEntries",
                column: "ReversesEntryId",
                unique: true,
                filter: "[ReversesEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_SavingsItemId_EffectiveDate_RecordedAtUtc_Id",
                table: "LedgerEntries",
                columns: new[] { "SavingsItemId", "EffectiveDate", "RecordedAtUtc", "Id" });

            // Existing totals have no payment history, so each funded item gets one inferred opening entry.
            migrationBuilder.Sql(@"
INSERT INTO [LedgerEntries] ([Id], [SavingsItemId], [Type], [Amount], [EffectiveDate], [Description], [RecordedAtUtc], [IsInferred])
SELECT NEWID(), [Id], 0, [AllocatedAmount], [StartDate], N'Opening balance inferred from existing total funding',
       TODATETIMEOFFSET(SYSUTCDATETIME(), 0), CAST(1 AS bit)
FROM [SavingsItems]
WHERE [AllocatedAmount] > 0;

INSERT INTO [LedgerEntries] ([Id], [SavingsItemId], [Type], [Amount], [EffectiveDate], [Description], [RecordedAtUtc], [IsInferred])
SELECT [Id], [SavingsItemId], 3, [Amount], [SpentOn], [Description], [RecordedAtUtc], CAST(0 AS bit)
FROM [Drawdowns];");

            migrationBuilder.DropTable(
                name: "Drawdowns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Drawdowns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SavingsItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpentOn = table.Column<DateOnly>(type: "date", nullable: false)
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

            // Rolling back keeps only drawdowns that are still effective; payment and reversal history is lost.
            migrationBuilder.Sql(@"
INSERT INTO [Drawdowns] ([Id], [SavingsItemId], [Amount], [SpentOn], [Description], [RecordedAtUtc])
SELECT d.[Id], d.[SavingsItemId], d.[Amount], d.[EffectiveDate], d.[Description], d.[RecordedAtUtc]
FROM [LedgerEntries] d
WHERE d.[Type] = 3 AND NOT EXISTS (SELECT 1 FROM [LedgerEntries] r WHERE r.[ReversesEntryId] = d.[Id]);");

            migrationBuilder.DropTable(
                name: "LedgerEntries");
        }
    }
}
