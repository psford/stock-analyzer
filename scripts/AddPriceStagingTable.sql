-- Migration: Add PriceStaging table for buffering incoming EODHD bulk data
-- Date: 2026-01-26
-- Description: Creates staging schema and PriceStaging table for high-throughput data ingestion

-- Create staging schema if not exists
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'staging')
BEGIN
    EXEC('CREATE SCHEMA staging');
    PRINT 'Created schema: staging';
END
GO

-- Create PriceStaging table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'staging.PriceStaging') AND type in (N'U'))
BEGIN
    CREATE TABLE staging.PriceStaging (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        BatchId UNIQUEIDENTIFIER NOT NULL,
        Ticker NVARCHAR(20) NOT NULL,
        EffectiveDate DATE NOT NULL,
        [Open] DECIMAL(18, 4) NOT NULL,
        High DECIMAL(18, 4) NOT NULL,
        Low DECIMAL(18, 4) NOT NULL,
        [Close] DECIMAL(18, 4) NOT NULL,
        AdjustedClose DECIMAL(18, 4) NULL,
        Volume BIGINT NULL,
        [Status] NVARCHAR(20) NOT NULL DEFAULT 'pending',
        ErrorMessage NVARCHAR(500) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ProcessedAt DATETIME2 NULL,
        CONSTRAINT PK_PriceStaging PRIMARY KEY CLUSTERED (Id)
    );
    PRINT 'Created table: staging.PriceStaging';
END
GO

-- Create indexes for efficient processing

-- Index for finding pending records to process
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PriceStaging_Status_CreatedAt' AND object_id = OBJECT_ID('staging.PriceStaging'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PriceStaging_Status_CreatedAt
    ON staging.PriceStaging ([Status], CreatedAt)
    INCLUDE (BatchId, Ticker, EffectiveDate);
    PRINT 'Created index: IX_PriceStaging_Status_CreatedAt';
END
GO

-- Index for batch processing and cleanup
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PriceStaging_BatchId' AND object_id = OBJECT_ID('staging.PriceStaging'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PriceStaging_BatchId
    ON staging.PriceStaging (BatchId)
    INCLUDE ([Status], CreatedAt, ProcessedAt);
    PRINT 'Created index: IX_PriceStaging_BatchId';
END
GO

-- Index for ticker lookups during merge
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PriceStaging_Ticker_EffectiveDate' AND object_id = OBJECT_ID('staging.PriceStaging'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_PriceStaging_Ticker_EffectiveDate
    ON staging.PriceStaging (Ticker, EffectiveDate)
    INCLUDE ([Status], [Open], High, Low, [Close], AdjustedClose, Volume);
    PRINT 'Created index: IX_PriceStaging_Ticker_EffectiveDate';
END
GO

-- Verify creation
SELECT
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType
FROM sys.tables t
LEFT JOIN sys.indexes i ON t.object_id = i.object_id AND i.type > 0
WHERE t.name = 'PriceStaging'
ORDER BY i.name;
GO

PRINT 'Migration complete: PriceStaging table and indexes created';
GO
