-- Seed script for Index Attribution tables
-- Run against local SQL Express: sqlcmd -S .\SQLEXPRESS -d StockAnalyzer -i seed_index_attribution.sql

-- Backfill SourceType for existing calendar source
UPDATE [data].Sources SET SourceType = 'Calendar' WHERE SourceId = 1 AND SourceType IS NULL;

-- Seed constituent data sources (10-19 range)
INSERT INTO [data].Sources (SourceId, SourceShortName, SourceLongName, SourceType, UpdatedBy)
VALUES
    (10, 'iShares',      'iShares by BlackRock',       'ConstituentData', 'system'),
    (11, 'SPDR',         'SPDR by State Street',       'ConstituentData', 'system'),
    (12, 'FTSERussell',  'FTSE Russell Official',      'ConstituentData', 'system'),
    (13, 'SlickCharts',  'SlickCharts.com',            'ConstituentData', 'system');

-- Seed composited/derived sources (20+ range)
INSERT INTO [data].Sources (SourceId, SourceShortName, SourceLongName, SourceType, UpdatedBy)
VALUES
    (20, 'GoldCopy',     'Composited Best Data',       'GoldCopy',        'system');

-- Seed IndexDefinition (7 benchmark indices)
INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, WeightingMethod, Region, ProxyEtfTicker)
VALUES
    ('SP500',      'S&P 500',                'S&P',     'FloatAdjustedMarketCap', 'US',                'IVV'),
    ('R1000',      'Russell 1000',           'Russell', 'FloatAdjustedMarketCap', 'US',                'IWB'),
    ('R2000',      'Russell 2000',           'Russell', 'FloatAdjustedMarketCap', 'US',                'IWM'),
    ('R3000',      'Russell 3000',           'Russell', 'FloatAdjustedMarketCap', 'US',                'IWV'),
    ('MSCI_EAFE',  'MSCI EAFE',             'MSCI',    'FloatAdjustedMarketCap', 'Developed ex-US',   'EFA'),
    ('MSCI_EM',    'MSCI Emerging Markets',  'MSCI',    'FloatAdjustedMarketCap', 'Emerging Markets',  'EEM'),
    ('MSCI_ACWI',  'MSCI ACWI',             'MSCI',    'FloatAdjustedMarketCap', 'Global',            'ACWI');

-- Seed benchmark chip indices (SPY, QQQ, DIA, EWU, EWG, EWJ) with IF NOT EXISTS guards
-- These complement the existing entries and support the chart benchmarks UI (Phase 3).
IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'SPY')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('SP500_SPY', 'S&P 500', 'S&P', 'US', 'SPY');

IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'QQQ')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('NDX100', 'Nasdaq-100', 'Nasdaq', 'US', 'QQQ');

IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'DIA')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('DJIA', 'Dow Jones Industrial Average', 'Dow Jones', 'US', 'DIA');

IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'EWU')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('FTSE100', 'FTSE 100', 'FTSE', 'GB', 'EWU');

IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'EWG')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('DAX40', 'DAX 40', 'DAX', 'DE', 'EWG');

IF NOT EXISTS (SELECT 1 FROM [data].IndexDefinition WHERE ProxyEtfTicker = 'EWJ')
    INSERT INTO [data].IndexDefinition (IndexCode, IndexName, IndexFamily, Region, ProxyEtfTicker)
    VALUES ('NKY225', 'Nikkei 225', 'Nikkei', 'JP', 'EWJ');

-- Verify
SELECT 'Sources' AS TableName, COUNT(*) AS [RowCount] FROM [data].Sources;
SELECT 'IndexDefinition' AS TableName, COUNT(*) AS [RowCount] FROM [data].IndexDefinition;
SELECT * FROM [data].Sources ORDER BY SourceId;
SELECT * FROM [data].IndexDefinition ORDER BY IndexId;
