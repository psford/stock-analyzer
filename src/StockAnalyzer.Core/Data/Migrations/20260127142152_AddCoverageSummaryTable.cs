using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverageSummaryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoverageSummary",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    ImportanceScore = table.Column<int>(type: "int", nullable: false),
                    TrackedRecords = table.Column<long>(type: "bigint", nullable: false),
                    UntrackedRecords = table.Column<long>(type: "bigint", nullable: false),
                    TrackedSecurities = table.Column<int>(type: "int", nullable: false),
                    UntrackedSecurities = table.Column<int>(type: "int", nullable: false),
                    TradingDays = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverageSummary", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoverageSummary_Year_Score",
                schema: "data",
                table: "CoverageSummary",
                columns: new[] { "Year", "ImportanceScore" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoverageSummary",
                schema: "data");
        }
    }
}
