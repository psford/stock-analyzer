using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityMasterAndPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "data");

            migrationBuilder.CreateTable(
                name: "SecurityMaster",
                schema: "data",
                columns: table => new
                {
                    SecurityAlias = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrimaryAssetId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IssueName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TickerSymbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Exchange = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SecurityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityMaster", x => x.SecurityAlias);
                });

            migrationBuilder.CreateTable(
                name: "Prices",
                schema: "data",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecurityAlias = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Volatility = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: true),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    AdjustedClose = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prices_SecurityMaster_SecurityAlias",
                        column: x => x.SecurityAlias,
                        principalSchema: "data",
                        principalTable: "SecurityMaster",
                        principalColumn: "SecurityAlias",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prices_EffectiveDate",
                schema: "data",
                table: "Prices",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_SecurityAlias_EffectiveDate",
                schema: "data",
                table: "Prices",
                columns: new[] { "SecurityAlias", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityMaster_IsActive",
                schema: "data",
                table: "SecurityMaster",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityMaster_TickerSymbol",
                schema: "data",
                table: "SecurityMaster",
                column: "TickerSymbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prices",
                schema: "data");

            migrationBuilder.DropTable(
                name: "SecurityMaster",
                schema: "data");
        }
    }
}
