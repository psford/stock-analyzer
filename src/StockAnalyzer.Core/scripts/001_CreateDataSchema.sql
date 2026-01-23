-- ============================================================================
-- Script: 001_CreateDataSchema.sql
-- Purpose: Create the 'data' schema for domain/analytical data
-- Description: Separates domain data (securities, prices) from operational
--              data (watchlists, caches) for better organization and security.
-- ============================================================================

-- Create schema for domain data (separate from operational tables in dbo)
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'data')
BEGIN
    EXEC('CREATE SCHEMA data')
    PRINT 'Created schema: data'
END
ELSE
BEGIN
    PRINT 'Schema already exists: data'
END
GO
