using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityMasterCountryCurrencyIsin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add columns only if they don't exist (idempotent migration)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[data].[SecurityMaster]') AND name = 'Country')
                BEGIN
                    ALTER TABLE [data].[SecurityMaster] ADD [Country] nvarchar(max) NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[data].[SecurityMaster]') AND name = 'Currency')
                BEGIN
                    ALTER TABLE [data].[SecurityMaster] ADD [Currency] nvarchar(max) NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('[data].[SecurityMaster]') AND name = 'Isin')
                BEGIN
                    ALTER TABLE [data].[SecurityMaster] ADD [Isin] nvarchar(max) NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "data",
                table: "SecurityMaster");

            migrationBuilder.DropColumn(
                name: "Isin",
                schema: "data",
                table: "SecurityMaster");
        }
    }
}
