using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcesAndBusinessCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sources",
                schema: "data",
                columns: table => new
                {
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceShortName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceLongName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sources", x => x.SourceId);
                });

            migrationBuilder.CreateTable(
                name: "BusinessCalendar",
                schema: "data",
                columns: table => new
                {
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: false),
                    IsBusinessDay = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsMonthEnd = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsLastBusinessDayMonthEnd = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessCalendar", x => new { x.SourceId, x.EffectiveDate });
                    table.ForeignKey(
                        name: "FK_BusinessCalendar_Sources_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "data",
                        principalTable: "Sources",
                        principalColumn: "SourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessCalendar_SourceId_IsBusinessDay_EffectiveDate",
                schema: "data",
                table: "BusinessCalendar",
                columns: new[] { "SourceId", "IsBusinessDay", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Sources_SourceShortName",
                schema: "data",
                table: "Sources",
                column: "SourceShortName",
                unique: true);

            // Seed the US source
            migrationBuilder.InsertData(
                schema: "data",
                table: "Sources",
                columns: new[] { "SourceId", "SourceShortName", "SourceLongName" },
                values: new object[] { 1, "US", "US Business Day Calendar" });

            // Note: Business calendar data will be populated via a separate seeding operation
            // after migration, using the UsMarketCalendar service for accurate holiday calculation.
            // Run: POST /api/admin/calendar/populate-us to populate the calendar.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessCalendar",
                schema: "data");

            migrationBuilder.DropTable(
                name: "Sources",
                schema: "data");
        }
    }
}
