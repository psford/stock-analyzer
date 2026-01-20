# Azure Deployment Guide

Deploy Stock Analyzer to Azure App Service with Azure SQL Database.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Azure Resource Group                     │
│                  (rg-stockanalyzer-prod)                    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐      ┌─────────────────────────────┐  │
│  │  App Service    │      │  Azure SQL Database         │  │
│  │  (B1 tier)      │◄────►│  (Basic 5 DTU - $4.99/mo)   │  │
│  │  Linux/Docker   │      │  stockanalyzer-db           │  │
│  └────────┬────────┘      └─────────────────────────────┘  │
│           │                                                 │
│  ┌─────────────────┐      ┌─────────────────────────────┐  │
│  │  Key Vault      │      │  Container Registry         │  │
│  │  (secrets)      │      │  (ghcr.io)                  │  │
│  └─────────────────┘      └─────────────────────────────┘  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Estimated Monthly Cost:** ~$18-20/month
- App Service B1: $13.14/month
- Azure SQL Basic: $4.99/month
- Key Vault: ~$0.03/10k operations
- Bandwidth: ~$0-2/month (first 5GB free)

---

## Prerequisites

1. **Azure Account** - Sign up at https://azure.microsoft.com/free/
   - $200 credit for 30 days (new accounts)
   - Free tier services for 12 months

2. **Azure CLI** - Install via Chocolatey:
   ```powershell
   choco install azure-cli -y
   ```

3. **GitHub Account** - For container registry (ghcr.io)

4. **Finnhub API Key** - Get free key at https://finnhub.io/

---

## Quick Start (Automated)

### 1. Login to Azure

```powershell
az login
```

### 2. Run Deployment Script

```powershell
cd stock_analyzer_dotnet/infrastructure/azure
.\deploy.ps1 -Environment prod -Location eastus
```

The script will:
- Create resource group
- Deploy all Azure resources via Bicep
- Prompt for SQL password and Finnhub API key
- Output deployment URLs

---

## Manual Deployment

### Step 1: Create Resource Group

```bash
az group create --name rg-stockanalyzer-prod --location eastus
```

### Step 2: Deploy Infrastructure

```bash
cd infrastructure/azure

az deployment group create \
  --resource-group rg-stockanalyzer-prod \
  --template-file main.bicep \
  --parameters environment=prod \
  --parameters sqlAdminPassword='<your-secure-password>' \
  --parameters finnhubApiKey='<your-finnhub-key>'
```

### Step 3: Build and Push Container

```bash
# Login to GitHub Container Registry
echo $GITHUB_TOKEN | docker login ghcr.io -u USERNAME --password-stdin

# Build image
cd stock_analyzer_dotnet
docker build -t ghcr.io/psford/stockanalyzer:latest .

# Push to registry
docker push ghcr.io/psford/stockanalyzer:latest
```

### Step 4: Deploy to App Service

```bash
az webapp config container set \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --docker-custom-image-name ghcr.io/psford/stockanalyzer:latest
```

### Step 5: Apply Database Migrations

The app automatically runs migrations on startup in Production. To run manually:

```bash
# Get connection string from Azure
CONNECTION_STRING=$(az webapp config connection-string list \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --query "[0].value" -o tsv)

# Run migrations
dotnet ef database update \
  --project src/StockAnalyzer.Core \
  --startup-project src/StockAnalyzer.Api \
  --connection "$CONNECTION_STRING"
```

---

## CI/CD with GitHub Actions

The repository includes automated deployment via `.github/workflows/azure-deploy.yml`.

### Setup Steps

1. **Create Azure Service Principal:**

   ```bash
   az ad sp create-for-rbac \
     --name "github-stockanalyzer" \
     --role contributor \
     --scopes /subscriptions/<subscription-id>/resourceGroups/rg-stockanalyzer-prod \
     --sdk-auth
   ```

2. **Add GitHub Secrets:**

   Go to repository Settings → Secrets and variables → Actions:
   - `AZURE_CREDENTIALS`: Output from step 1 (JSON)

3. **Trigger Deployment:**

   Push to `master` or `main` branch, or use workflow dispatch.

---

## Configuration

### App Settings

| Setting | Description | Source |
|---------|-------------|--------|
| `ConnectionStrings__DefaultConnection` | Azure SQL connection | Bicep template |
| `Finnhub__ApiKey` | News API key | Key Vault reference |
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | Set to `Production` |
| `WEBSITES_PORT` | Container port | Set to `5000` |
| `RUN_MIGRATIONS` | Auto-apply migrations | Set to `true` |

### Custom Domain (Optional)

1. Go to App Service → Custom domains
2. Add your domain
3. Azure provides free managed SSL certificate

---

## Monitoring

### Health Checks

- **Full health:** `https://app-stockanalyzer-prod.azurewebsites.net/health`
- **Liveness:** `https://app-stockanalyzer-prod.azurewebsites.net/health/live`
- **Readiness:** `https://app-stockanalyzer-prod.azurewebsites.net/health/ready`

### Logs

```bash
# Stream live logs
az webapp log tail \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod

# Download log files
az webapp log download \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --log-file logs.zip
```

### Application Insights (Optional)

Enable in Azure Portal for:
- Request tracing
- Performance monitoring
- Error tracking
- Custom metrics

---

## Scaling

### Vertical Scaling (Scale Up)

Change App Service Plan tier:

```bash
az appservice plan update \
  --name asp-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --sku S1  # Standard tier
```

### Horizontal Scaling (Scale Out)

Add more instances:

```bash
az appservice plan update \
  --name asp-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --number-of-workers 2
```

### Database Scaling

Upgrade SQL tier:

```bash
az sql db update \
  --name stockanalyzer-db \
  --server sql-stockanalyzer-prod-<unique> \
  --resource-group rg-stockanalyzer-prod \
  --edition Standard \
  --capacity 10  # DTUs
```

---

## Troubleshooting

### Container Won't Start

1. Check container logs:
   ```bash
   az webapp log tail --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod
   ```

2. Verify image exists:
   ```bash
   docker pull ghcr.io/psford/stockanalyzer:latest
   ```

3. Check app settings for typos

### Database Connection Fails

1. Verify firewall allows Azure services:
   ```bash
   az sql server firewall-rule create \
     --name AllowAzureServices \
     --server sql-stockanalyzer-prod-<unique> \
     --resource-group rg-stockanalyzer-prod \
     --start-ip-address 0.0.0.0 \
     --end-ip-address 0.0.0.0
   ```

2. Test connection string locally

### Slow Performance

1. Check DTU usage in Azure Portal
2. Consider upgrading SQL tier
3. Enable Application Insights for profiling

---

## Cost Optimization

### Development Environment

Use free/cheaper tiers:
- App Service: F1 (Free) - limited to 60 min/day
- SQL: Serverless (auto-pause when idle)

### Production

- B1 tier is cost-effective for low traffic
- Use Reserved Instances for 1-3 year commitment (up to 72% savings)
- Enable auto-shutdown for non-prod environments

---

## Cleanup

Remove all resources:

```bash
az group delete --name rg-stockanalyzer-prod --yes --no-wait
```

---

## Version History

| Date | Change |
|------|--------|
| 01/19/2026 | Updated for App Service B1 migration, Key Vault integration |
| 01/17/2026 | Initial Azure deployment guide |
