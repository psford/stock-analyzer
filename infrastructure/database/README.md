# Database Deployment

## Overview

The Stock Analyzer database contains pre-loaded historical price data for S&P 500 stocks (1980-2026). This data should be deployed WITH the application to avoid expensive API calls for bulk historical backfills in production.

## Database Contents

| Table | Rows | Description |
|-------|------|-------------|
| `data.Prices` | 3,556,127 | Historical OHLC price data (S&P 500, 1980-2026) |
| `data.SecurityMaster` | 29,874 | Security reference with auto-incrementing aliases |
| `dbo.Symbols` | 29,878 | Symbol lookup cache from Finnhub |
| `dbo.CachedImages` | 2,000 | Pre-cached cat/dog images |

**Total Database Size:** ~976 MB (uncompressed), ~125 MB (BACPAC compressed)

## Exporting the Database

To create a fresh BACPAC export from your local SQL Express:

```powershell
# Ensure SqlPackage is installed
winget install Microsoft.SqlPackage

# Export database
sqlpackage /Action:Export `
    /SourceServerName:".\SQLEXPRESS" `
    /SourceDatabaseName:"StockAnalyzer" `
    /TargetFile:"StockAnalyzer.bacpac" `
    /p:CompressionOption:Maximum `
    /SourceTrustServerCertificate:True
```

## Importing to Azure SQL

### Option 1: Azure Portal

1. Upload BACPAC to Azure Blob Storage
2. In Azure Portal → SQL Database → Import database
3. Select the BACPAC from blob storage
4. Configure server, authentication, and pricing tier

### Option 2: SqlPackage CLI

```powershell
sqlpackage /Action:Import `
    /TargetServerName:"your-server.database.windows.net" `
    /TargetDatabaseName:"StockAnalyzer" `
    /TargetUser:"your-admin" `
    /TargetPassword:"your-password" `  # pragma: allowlist secret
    /SourceFile:"StockAnalyzer.bacpac"
```

### Option 3: Azure CLI

```powershell
# Upload BACPAC to blob storage
az storage blob upload `
    --account-name yourstorageaccount `
    --container-name backups `
    --name StockAnalyzer.bacpac `
    --file StockAnalyzer.bacpac

# Import to Azure SQL
az sql db import `
    --resource-group your-rg `
    --server your-server `
    --name StockAnalyzer `
    --storage-key-type StorageAccessKey `
    --storage-key "your-storage-key" `
    --storage-uri "https://yourstorageaccount.blob.core.windows.net/backups/StockAnalyzer.bacpac" `
    --admin-user your-admin `
    --admin-password your-password  # pragma: allowlist secret
```

## Production Data Updates

After initial deployment, the following should run automatically:

1. **Daily Price Refresh** (2:30 AM UTC) - `PriceRefreshService` background job
   - Uses EODHD bulk API to fetch previous day's prices
   - Only updates existing securities in SecurityMaster
   - Cost: 1 API call per day

2. **Symbol Refresh** (2:00 AM UTC) - `SymbolRefreshService` background job
   - Syncs active symbols from Finnhub
   - Cost: Minimal API usage

## What NOT to Run in Production

- **Bulk historical backfills** (`/api/admin/prices/load-tickers` with large date ranges)
  - These should only be run locally and exported via BACPAC
  - 500 stocks × 10 years = ~1.26M API calls to EODHD

- **Full S&P 500 backfills**
  - Run locally, export to BACPAC, import to production

## File Storage

The BACPAC file is NOT committed to git (too large). Store in:

- **Azure Blob Storage** (recommended) - `stockanalyzerbackups` container
- **GitHub Releases** - As a release artifact
- **Local backup** - `C:\Backups\StockAnalyzer\`

## Regenerating After Data Changes

When you add significant new data locally:

1. Run the backfill locally
2. Export new BACPAC
3. Upload to Azure Blob Storage
4. Import to production (or swap databases)
