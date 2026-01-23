using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedSentiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CachedSentiments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeadlineHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Sentiment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    PositiveProb = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    NegativeProb = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    NeutralProb = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    AnalyzerVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    IsPending = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CachedSentiments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CachedSentiments_HeadlineHash",
                table: "CachedSentiments",
                column: "HeadlineHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CachedSentiments_Pending",
                table: "CachedSentiments",
                columns: new[] { "IsPending", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CachedSentiments");
        }
    }
}
