using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateIndexTablesIfNotExist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent migration: creates tables only if they don't exist.
            // Locally, these tables were created by the Python iShares pipeline.
            // In production, they need to be created by this migration.

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'data' AND t.name = 'IndexDefinition')
                BEGIN
                    CREATE TABLE [data].[IndexDefinition] (
                        [IndexId] int NOT NULL IDENTITY(1,1),
                        [IndexCode] nvarchar(20) NOT NULL,
                        [IndexName] nvarchar(200) NOT NULL,
                        [IndexFamily] nvarchar(50) NULL,
                        [WeightingMethod] nvarchar(50) NULL,
                        [Region] nvarchar(100) NULL,
                        [ProxyEtfTicker] nvarchar(20) NULL,
                        CONSTRAINT [PK_IndexDefinition] PRIMARY KEY ([IndexId])
                    );
                    CREATE UNIQUE INDEX [IX_IndexDefinition_IndexCode] ON [data].[IndexDefinition] ([IndexCode]);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'data' AND t.name = 'IndexConstituent')
                BEGIN
                    CREATE TABLE [data].[IndexConstituent] (
                        [Id] bigint NOT NULL IDENTITY(1,1),
                        [IndexId] int NOT NULL,
                        [SecurityAlias] int NOT NULL,
                        [EffectiveDate] date NOT NULL,
                        [Weight] decimal(18,8) NULL,
                        [MarketValue] decimal(18,2) NULL,
                        [Shares] decimal(18,4) NULL,
                        [Sector] nvarchar(100) NULL,
                        [Location] nvarchar(100) NULL,
                        [Currency] nvarchar(10) NULL,
                        [SourceId] int NOT NULL,
                        [SourceTicker] nvarchar(20) NULL,
                        CONSTRAINT [PK_IndexConstituent] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_IndexConstituent_IndexDefinition_IndexId] FOREIGN KEY ([IndexId]) REFERENCES [data].[IndexDefinition] ([IndexId]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_IndexConstituent_SecurityMaster_SecurityAlias] FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster] ([SecurityAlias]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_IndexConstituent_Sources_SourceId] FOREIGN KEY ([SourceId]) REFERENCES [data].[Sources] ([SourceId]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_IndexConstituent_IndexDate] ON [data].[IndexConstituent] ([IndexId], [EffectiveDate]);
                    CREATE INDEX [IX_IndexConstituent_SecurityAlias] ON [data].[IndexConstituent] ([SecurityAlias]);
                    CREATE INDEX [IX_IndexConstituent_SourceId] ON [data].[IndexConstituent] ([SourceId]);
                    CREATE UNIQUE INDEX [IX_IndexConstituent_Unique] ON [data].[IndexConstituent] ([IndexId], [SecurityAlias], [EffectiveDate], [SourceId]);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'data' AND t.name = 'SecurityIdentifier')
                BEGIN
                    CREATE TABLE [data].[SecurityIdentifier] (
                        [SecurityAlias] int NOT NULL,
                        [IdentifierType] nvarchar(20) NOT NULL,
                        [IdentifierValue] nvarchar(50) NOT NULL,
                        [SourceId] int NOT NULL,
                        [UpdatedBy] nvarchar(100) NULL,
                        [UpdatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
                        CONSTRAINT [PK_SecurityIdentifier] PRIMARY KEY ([SecurityAlias], [IdentifierType]),
                        CONSTRAINT [FK_SecurityIdentifier_SecurityMaster_SecurityAlias] FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster] ([SecurityAlias]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_SecurityIdentifier_TypeValue] ON [data].[SecurityIdentifier] ([IdentifierType], [IdentifierValue]);
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'data' AND t.name = 'SecurityIdentifierHist')
                BEGIN
                    CREATE TABLE [data].[SecurityIdentifierHist] (
                        [Id] bigint NOT NULL IDENTITY(1,1),
                        [SecurityAlias] int NOT NULL,
                        [IdentifierType] nvarchar(20) NOT NULL,
                        [IdentifierValue] nvarchar(50) NOT NULL,
                        [EffectiveFrom] date NOT NULL,
                        [EffectiveTo] date NOT NULL,
                        [SourceId] int NOT NULL,
                        CONSTRAINT [PK_SecurityIdentifierHist] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_SecurityIdentifierHist_SecurityMaster_SecurityAlias] FOREIGN KEY ([SecurityAlias]) REFERENCES [data].[SecurityMaster] ([SecurityAlias]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_SecurityIdentifierHist_AliasType] ON [data].[SecurityIdentifierHist] ([SecurityAlias], [IdentifierType]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse dependency order
            migrationBuilder.Sql("IF OBJECT_ID('data.IndexConstituent', 'U') IS NOT NULL DROP TABLE [data].[IndexConstituent];");
            migrationBuilder.Sql("IF OBJECT_ID('data.SecurityIdentifier', 'U') IS NOT NULL DROP TABLE [data].[SecurityIdentifier];");
            migrationBuilder.Sql("IF OBJECT_ID('data.SecurityIdentifierHist', 'U') IS NOT NULL DROP TABLE [data].[SecurityIdentifierHist];");
            migrationBuilder.Sql("IF OBJECT_ID('data.IndexDefinition', 'U') IS NOT NULL DROP TABLE [data].[IndexDefinition];");
        }
    }
}
