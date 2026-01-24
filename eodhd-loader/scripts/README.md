# Production Backfill Scripts

Scripts for efficiently backfilling historical price data to production Azure SQL database via Stock Analyzer API.

---

## Backfill-Production.ps1

### Purpose

Loads ~4 years of historical price data for all securities in the database to production using the EODHD Bulk API approach.

**Target**: 32,737 securities identified as needing backfill
**Method**: Bulk API by date (processes all securities per trading day)
**Expected Duration**: ~28 hours
**Cost**: ~100,000 EODHD API calls (100k plan limit)

### Prerequisites

1. **Stock Analyzer API** running at `https://psfordtaurus.com`
2. **EODHD API subscription** configured on the server
3. **PowerShell 5.1+** or PowerShell Core 7+
4. **Network access** to production API

### Basic Usage

```powershell
# Default: Backfill last 5 years to yesterday
.\Backfill-Production.ps1

# Custom date range
.\Backfill-Production.ps1 -FromDate "2020-01-01" -ToDate "2024-12-31"

# Dry run (test without making API calls)
.\Backfill-Production.ps1 -DryRun

# Resume from checkpoint
.\Backfill-Production.ps1
# (automatically detects and resumes from backfill-checkpoint.json)
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `FromDate` | DateTime | 1000 days ago (~4 years) | Start date for backfill |
| `ToDate` | DateTime | Yesterday | End date for backfill |
| `ApiUrl` | String | `https://psfordtaurus.com` | Stock Analyzer API URL |
| `CheckpointFile` | String | `backfill-checkpoint.json` | Checkpoint file path |
| `DryRun` | Switch | `$false` | Log actions without API calls |

### How It Works

1. **Generates Trading Days**: Creates list of weekdays (Mon-Fri) in date range (max 1000 days for 100k API limit)
2. **Calls Bulk API**: For each day, calls `/api/admin/prices/bulk-load` with date
3. **API Orchestrates**: Stock Analyzer API fetches data from EODHD Bulk endpoint and saves to Azure SQL
4. **Progress Tracking**: Logs every request, saves checkpoint every 10 days
5. **Error Handling**: Retries failed requests 3 times with 5-second backoff
6. **Rate Limiting**: 1-second delay between requests to avoid overwhelming API

### Expected Output

```
[2025-01-24 10:00:00] [INFO] === Production Backfill Started ===
[2025-01-24 10:00:00] [INFO] Target API: https://psfordtaurus.com
[2025-01-24 10:00:00] [INFO] Date Range: 2021-02-24 to 2025-01-23
[2025-01-24 10:00:00] [INFO] Dry Run: False
[2025-01-24 10:00:01] [INFO] Generated 1000 trading days from 2021-02-24 to 2025-01-23
[2025-01-24 10:00:01] [INFO] Processing 1000 trading days...
[2025-01-24 10:00:01] [INFO] [1/1000] (0.10%) Processing 2021-02-24...
[2025-01-24 10:00:05] [INFO] SUCCESS: 2021-02-24 - Loaded 25000 prices, 100 API calls
[2025-01-24 10:00:06] [INFO] [2/1000] (0.20%) Processing 2021-02-25...
...
[2025-01-25 14:00:00] [INFO] Checkpoint saved: 2025-01-23
[2025-01-25 14:00:00] [INFO] === Production Backfill Complete ===
[2025-01-25 14:00:00] [INFO] Days Processed: 1000
[2025-01-25 14:00:00] [INFO] API Calls Made: 100000
[2025-01-25 14:00:00] [INFO] Prices Loaded: 25000000
[2025-01-25 14:00:00] [INFO] Errors: 0
```

### Checkpoint & Resume

The script saves progress every 10 days to `backfill-checkpoint.json`. If the script is interrupted (network outage, machine restart, etc.), simply re-run it:

```powershell
.\Backfill-Production.ps1
```

It will automatically detect the checkpoint and resume from the last processed date.

**Checkpoint Structure:**
```json
{
  "LastProcessedDate": "2024-06-15",
  "TotalDaysProcessed": 650,
  "TotalApiCalls": 65000,
  "TotalPricesLoaded": 16250000,
  "TotalErrors": 2,
  "CheckpointTime": "2025-01-25T14:30:00.000Z"
}
```

To start fresh (ignore checkpoint), delete or rename the checkpoint file before running.

### Logs

Each run creates a timestamped log file: `backfill-YYYYMMDD-HHMMSS.log`

Logs include:
- Every API request (date, result, prices loaded, API calls made)
- Checkpoint saves
- Errors and retry attempts
- Final summary statistics

### Error Handling

**Retry Logic:**
- Each failed request is retried 3 times with 5-second delay
- After 3 failures, logs error and continues to next day
- Failed days are counted in TotalErrors but don't stop execution

**Common Errors:**

| Error | Cause | Solution |
|-------|-------|----------|
| `Connection refused` | API not running | Verify API is accessible at `https://psfordtaurus.com` |
| `401 Unauthorized` | Missing auth | Check API authentication configuration |
| `503 Service Unavailable` | EODHD API limit | Wait and resume; checkpoint preserves progress |
| `Timeout` | Slow network/large response | Script retries automatically |

### Production Safety

**Before Running:**

1. ✅ Verify API endpoint: `curl https://psfordtaurus.com/health`
2. ✅ Test with dry run first: `.\Backfill-Production.ps1 -DryRun`
3. ✅ Confirm EODHD API key is configured on server
4. ✅ Check Azure SQL database has capacity for ~32M new price records
5. ✅ Notify stakeholders of 2-day backfill operation

**During Execution:**

- Monitor log file for errors: `Get-Content backfill-*.log -Wait`
- Check checkpoint progress periodically
- Avoid interrupting unless necessary (checkpoint enables safe resume)

**After Completion:**

1. Review final statistics in log
2. Verify data in production database
3. Archive checkpoint and log files for records
4. Update documentation with actual execution time and results

### Stopping & Resuming

**To Stop:**
- Press `Ctrl+C` in PowerShell window
- Script saves checkpoint before exiting
- Or kill the PowerShell process (checkpoint saved every 10 days)

**To Resume:**
- Simply re-run the script
- Checkpoint is automatically detected and loaded
- Execution continues from last saved date

### Alternative: Local Testing

To test against localhost Stock Analyzer API:

```powershell
.\Backfill-Production.ps1 -ApiUrl "http://localhost:5000" -FromDate "2024-01-01" -ToDate "2024-01-31"
```

This loads data to local SQL Server Express instead of production Azure SQL.

---

## Architecture Flow

```
PowerShell Script
    ↓
    POST /api/admin/prices/bulk-load?date=YYYY-MM-DD
    ↓
Stock Analyzer API (https://psfordtaurus.com)
    ↓
    GET https://eodhd.com/api/eod-bulk-last-day/US?date=YYYY-MM-DD
    ↓
EODHD API (~100 calls per date, ~130K total)
    ↓
    Returns JSON with all US securities' prices for that date
    ↓
Stock Analyzer API saves to Azure SQL Database
```

**Benefits of This Approach:**
- Centralized EODHD API key (server-side only)
- No direct database access from client
- Efficient bulk loading (all securities per day)
- API handles deduplication, validation, exchange mapping
- Checkpoint/resume capability for reliability

---

## Monitoring

### Real-Time Progress

```powershell
# Watch log file
Get-Content backfill-*.log -Wait

# Check latest checkpoint
Get-Content backfill-checkpoint.json | ConvertFrom-Json
```

### Expected Timeline

| Milestone | Days Processed | API Calls | Elapsed Time |
|-----------|----------------|-----------|--------------|
| 25% Complete | 250 | 25,000 | ~7 hours |
| 50% Complete | 500 | 50,000 | ~14 hours |
| 75% Complete | 750 | 75,000 | ~21 hours |
| 100% Complete | 1,000 | 100,000 | ~28 hours |

*Assumes 1-second rate limit, no failures, ~3,600 requests/hour*

### Database Growth

Expected new records: ~25,000,000 prices (32,737 securities × 1,000 days × ~75% availability)

Monitor Azure SQL database size during execution to ensure adequate capacity.

---

## Troubleshooting

### Script Won't Start

```powershell
# Check execution policy
Get-ExecutionPolicy

# If restricted, allow scripts
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### API Unreachable

```powershell
# Test API connectivity
Invoke-WebRequest -Uri "https://psfordtaurus.com/health"

# Check DNS resolution
Resolve-DnsName psfordtaurus.com
```

### High Error Rate

If `TotalErrors` exceeds 5% of days processed:
1. Check EODHD API subscription status
2. Verify server-side API key configuration
3. Review server logs for backend errors
4. Consider reducing rate limit (increase `$RateLimitDelayMs`)

### Script Hangs

- Check network connectivity
- Review API timeout setting (currently 300 seconds)
- Kill and restart; checkpoint enables resume

---

## Post-Backfill Verification

After completion, verify data in production:

```sql
-- Check total prices loaded
SELECT COUNT(*) FROM Prices;

-- Check date range coverage
SELECT
    MIN(EffectiveDate) AS EarliestDate,
    MAX(EffectiveDate) AS LatestDate
FROM Prices;

-- Check securities with data
SELECT COUNT(DISTINCT SecurityId) FROM Prices;

-- Expected: ~32,737 securities with prices from ~4 years ago to present
```

---

## Future Enhancements

Potential improvements for future versions:

- [ ] Parallel processing (multiple dates simultaneously)
- [ ] Email notifications on completion or critical errors
- [ ] Azure DevOps pipeline integration
- [ ] Incremental mode (only backfill missing dates)
- [ ] Slack notifications via webhook
- [ ] Progress dashboard (real-time web UI)
