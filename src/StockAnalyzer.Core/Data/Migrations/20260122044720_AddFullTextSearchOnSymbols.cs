using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextSearchOnSymbols : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Full-Text Catalog (container for full-text indexes)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'StockAnalyzerCatalog')
                BEGIN
                    CREATE FULLTEXT CATALOG StockAnalyzerCatalog AS DEFAULT;
                END
            ");

            // Create Full-Text Index on Symbols.Description
            // KEY INDEX must reference the primary key (Symbol column)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Symbols'))
                BEGIN
                    CREATE FULLTEXT INDEX ON Symbols(Description)
                    KEY INDEX PK_Symbols
                    ON StockAnalyzerCatalog
                    WITH CHANGE_TRACKING AUTO;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop Full-Text Index first
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Symbols'))
                BEGIN
                    DROP FULLTEXT INDEX ON Symbols;
                END
            ");

            // Drop Full-Text Catalog
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'StockAnalyzerCatalog')
                BEGIN
                    DROP FULLTEXT CATALOG StockAnalyzerCatalog;
                END
            ");
        }
    }
}
