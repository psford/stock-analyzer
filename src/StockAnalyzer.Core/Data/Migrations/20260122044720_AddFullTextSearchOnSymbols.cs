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
            // suppressTransaction: true required because CREATE FULLTEXT CATALOG cannot run inside a transaction
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'StockAnalyzerCatalog')
                BEGIN
                    CREATE FULLTEXT CATALOG StockAnalyzerCatalog AS DEFAULT;
                END
            ", suppressTransaction: true);

            // Create Full-Text Index on Symbols.Description
            // KEY INDEX must reference the primary key (Symbol column)
            // suppressTransaction: true required because CREATE FULLTEXT INDEX cannot run inside a transaction
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Symbols'))
                BEGIN
                    CREATE FULLTEXT INDEX ON Symbols(Description)
                    KEY INDEX PK_Symbols
                    ON StockAnalyzerCatalog
                    WITH CHANGE_TRACKING AUTO;
                END
            ", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop Full-Text Index first
            // suppressTransaction: true for consistency with Up()
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Symbols'))
                BEGIN
                    DROP FULLTEXT INDEX ON Symbols;
                END
            ", suppressTransaction: true);

            // Drop Full-Text Catalog
            // suppressTransaction: true for consistency with Up()
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'StockAnalyzerCatalog')
                BEGIN
                    DROP FULLTEXT CATALOG StockAnalyzerCatalog;
                END
            ", suppressTransaction: true);
        }
    }
}
