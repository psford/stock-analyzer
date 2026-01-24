# EODHD Data Loader

A standalone Windows WPF application for managing stock index constituents and efficiently loading historical price data to production databases. This tool allows you to lookup major indices (Russell 3000, S&P 500, MSCI, etc.) and trigger targeted backfills for only the relevant securities.

## Features

### Index Manager (Primary Feature)
- Select from major indices: S&P 500, Russell 3000, Russell 2000, Dow Jones, NASDAQ, MSCI, FTSE, DAX, CAC 40, Nikkei
- Fetch current index constituents from EODHD Fundamentals API
- Environment selector: Local (testing) or Production (Azure)
- Configure backfill date range
- Trigger backfills via Stock Analyzer API for index constituents only
- Production confirmation dialog with details
- Real-time progress tracking and logging
- Test connection to verify API availability

### Dashboard
- View total securities in the database
- See coverage statistics (securities with/without price data)
- Identify gaps in price data by security
- View coverage breakdown by security type

### Data Loader (Legacy)
- Load historical price data from EODHD API
- Select date range for bulk loading
- Real-time progress tracking
- Cancellation support

### Azure Migration (Legacy)
- Bulk copy data from local SQL Server to Azure SQL
- Configure batch size for optimal performance
- Progress tracking with transfer rate
- Incremental sync support (load only data after a specific date)

## Prerequisites

1. **.NET 8.0 SDK** - Required to build and run the application
2. **Stock Analyzer API** - Running locally (http://localhost:5000) or deployed to production (https://psfordtaurus.com)
3. **SQL Server Express** - Local database at `.\SQLEXPRESS` (for local testing)
4. **Azure SQL Database** - Production database (for production backfills)
5. **EODHD API Key** - Required for fetching index constituents and price data

## Configuration

The application looks for a `.env` file in the following locations (in order):
1. Application directory
2. `c:\Users\patri\Documents\claudeProjects\projects\eodhd-loader\.env`
3. `c:\Users\patri\Documents\claudeProjects\projects\stock-analyzer\.env`
4. `c:\Users\patri\Documents\claudeProjects\.env`

### Required Environment Variables

```env
EODHD_API_KEY=your_eodhd_api_key_here
```

### Environment-Specific Variables

**For Local Testing:**
```env
LOCAL_SQL_CONNECTION=Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;TrustServerCertificate=True
LOCAL_API_URL=http://localhost:5000
```

**For Production:**
```env
PROD_SQL_CONNECTION=Server=yourserver.database.windows.net;Database=StockAnalyzer;User Id=...;Password=...;Encrypt=True
PROD_API_URL=https://psfordtaurus.com
```

If not specified, defaults are:
- `LOCAL_SQL_CONNECTION`: `Server=.\SQLEXPRESS;Database=StockAnalyzer;Trusted_Connection=True;TrustServerCertificate=True`
- `LOCAL_API_URL`: `http://localhost:5000`

## Building

```powershell
cd c:\Users\patri\Documents\claudeProjects\projects\eodhd-loader
dotnet build
```

## Running

```powershell
cd c:\Users\patri\Documents\claudeProjects\projects\eodhd-loader\src\EodhdLoader
dotnet run
```

Or after building, run the executable directly:
```powershell
.\src\EodhdLoader\bin\Debug\net8.0-windows\EodhdLoader.exe
```

## Usage

### Index Manager (Recommended Workflow)

**Purpose:** Efficiently backfill historical price data for specific index constituents to production or local databases.

1. **Select Environment**
   - Choose "Local" for testing with localhost Stock Analyzer API
   - Choose "Production" to push data to Azure SQL via production API
   - Click "Test Connection" to verify API availability

2. **Select Index**
   - Choose from dropdown: S&P 500, Russell 3000, Dow Jones, NASDAQ, etc.
   - Click "Load Constituents" to fetch current member securities from EODHD

3. **Configure Backfill**
   - Set "From Date" (e.g., 5 years ago)
   - Set "To Date" (typically today)
   - Review constituent count

4. **Execute Backfill**
   - Click "Start Backfill"
   - For Production: Confirm in dialog showing target URL and security count
   - Monitor progress in log window
   - View success/error counts upon completion

**API Flow:**
- Index Manager → Stock Analyzer API `/api/admin/prices/load-tickers`
- API orchestrates backfill to configured database
- No local database writes by this tool (API-first architecture)

### Data Loader (Legacy Direct Mode)

1. Go to the **Data Loader** tab
2. Select the start and end dates for the data range
3. Click **Start Loading**
4. Monitor progress in the log window
5. Use **Cancel** to stop the operation if needed

### Azure Migration (Legacy Bulk Copy)

1. Go to the **Azure Migration** tab
2. Enter your Azure SQL connection string
3. Click **Test Connections** to verify connectivity
4. Select which tables to migrate (SecurityMaster, Prices)
5. Optionally enable incremental sync and set the from-date
6. Adjust batch size if needed (default: 5000)
7. Click **Start Migration**

## Architecture

**API-First Hybrid Design:**
- **Primary Flow:** EODHD Loader → Stock Analyzer API → Database
- **Fallback Flow:** Direct database access for Dashboard and legacy features

**Technology Stack:**
- **WPF** with MVVM pattern
- **CommunityToolkit.Mvvm** for INPC and commands
- **HttpClient** for Stock Analyzer API communication
- **StockAnalyzer.Core** for database access (Dashboard only)
- **EODHD Fundamentals API** for index constituent data
- **Environment-aware configuration** (Local vs Production)

**Benefits:**
- Centralized backfill logic in Stock Analyzer API
- Production-safe with confirmation dialogs
- Efficient: Only backfill relevant securities, not all 55k+
- Testable locally before production runs
- No direct production database access from desktop tool

## Project Structure

```
eodhd-loader/
├── EodhdLoader.sln
├── README.md
└── src/EodhdLoader/
    ├── App.xaml(.cs)                 # Application startup and DI
    ├── MainWindow.xaml(.cs)          # Main window with tabs
    ├── Converters.cs                 # WPF value converters
    ├── ViewModels/
    │   ├── ViewModelBase.cs
    │   ├── MainViewModel.cs
    │   ├── IndexManagerViewModel.cs  # Primary: Index-based backfills
    │   ├── DashboardViewModel.cs     # Database statistics
    │   ├── LoaderViewModel.cs        # Legacy: Direct loading
    │   └── MigrationViewModel.cs     # Legacy: Bulk copy
    ├── Views/
    │   ├── IndexManagerView.xaml(.cs)
    │   ├── DashboardView.xaml(.cs)
    │   ├── LoaderView.xaml(.cs)
    │   └── MigrationView.xaml(.cs)
    └── Services/
        ├── ConfigurationService.cs       # Environment & connection config
        ├── StockAnalyzerApiClient.cs     # HTTP client for Stock Analyzer API
        ├── IndexService.cs               # EODHD index constituent fetching
        ├── DataAnalysisService.cs        # Database statistics
        └── BulkCopyService.cs            # SqlBulkCopy for migration
```

## Supported Indices

- **S&P 500** (GSPC.INDX)
- **Russell 3000** (RUA.INDX)
- **Russell 2000** (RUT.INDX)
- **Dow Jones Industrial Average** (DJI.INDX)
- **NASDAQ Composite** (IXIC.INDX)
- **FTSE 100** (FTSE.INDX)
- **DAX** (GDAXI.INDX)
- **CAC 40** (FCHI.INDX)
- **Nikkei 225** (N225.INDX)
