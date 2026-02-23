using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyzer.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class MapIndexAttributionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Baseline migration: IndexDefinition, IndexConstituent, SecurityIdentifier,
            // and SecurityIdentifierHist tables already exist (created by Python pipeline).
            // This migration registers EF Core entity mappings without altering the schema.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: these tables are managed externally and should not be dropped.
        }
    }
}
