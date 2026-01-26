using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceStagingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "staging");

            migrationBuilder.CreateTable(
                name: "PriceStaging",
                schema: "staging",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ticker = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustedClose = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceStaging", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceStaging_BatchId",
                schema: "staging",
                table: "PriceStaging",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceStaging_Status_CreatedAt",
                schema: "staging",
                table: "PriceStaging",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceStaging_Ticker_EffectiveDate",
                schema: "staging",
                table: "PriceStaging",
                columns: new[] { "Ticker", "EffectiveDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceStaging",
                schema: "staging");
        }
    }
}
