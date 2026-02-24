using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateCoverageTablesIfNotExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT * FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = 'data' AND t.name = 'SecurityPriceCoverage')
        BEGIN
            CREATE TABLE [data].[SecurityPriceCoverage] (
                [SecurityAlias]    int          NOT NULL,
                [PriceCount]       int          NOT NULL DEFAULT 0,
                [FirstDate]        date         NULL,
                [LastDate]         date         NULL,
                [ExpectedCount]    int          NULL,
                [GapDays]          AS (ISNULL([ExpectedCount], 0) - [PriceCount]) PERSISTED,
                [LastUpdatedAt]    datetime2    NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_SecurityPriceCoverage] PRIMARY KEY ([SecurityAlias]),
                CONSTRAINT [FK_SecurityPriceCoverage_SecurityMaster]
                    FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster]([SecurityAlias])
                    ON DELETE CASCADE
            );
        END
    ");

            migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT * FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = 'data' AND t.name = 'SecurityPriceCoverageByYear')
        BEGIN
            CREATE TABLE [data].[SecurityPriceCoverageByYear] (
                [SecurityAlias]    int          NOT NULL,
                [Year]             int          NOT NULL,
                [PriceCount]       int          NOT NULL DEFAULT 0,
                [LastUpdatedAt]    datetime2    NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_SecurityPriceCoverageByYear] PRIMARY KEY ([SecurityAlias], [Year]),
                CONSTRAINT [FK_SecurityPriceCoverageByYear_SecurityMaster]
                    FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster]([SecurityAlias])
                    ON DELETE CASCADE
            );
        END
    ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        IF OBJECT_ID('[data].[SecurityPriceCoverageByYear]', 'U') IS NOT NULL
            DROP TABLE [data].[SecurityPriceCoverageByYear];
    ");

            migrationBuilder.Sql(@"
        IF OBJECT_ID('[data].[SecurityPriceCoverage]', 'U') IS NOT NULL
            DROP TABLE [data].[SecurityPriceCoverage];
    ");
        }
    }
}
