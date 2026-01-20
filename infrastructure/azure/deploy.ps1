# Azure Deployment Script for Stock Analyzer
# Run this after setting up your Azure account and logging in with 'az login'

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "prod",

    [Parameter(Mandatory=$false)]
    [string]$Location = "eastus",

    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "rg-stockanalyzer-prod"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Stock Analyzer Azure Deployment ===" -ForegroundColor Cyan

# Check if logged in
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "Please login to Azure first: az login" -ForegroundColor Red
    exit 1
}

Write-Host "Deploying to subscription: $($account.name)" -ForegroundColor Yellow
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "Location: $Location" -ForegroundColor Yellow

# Create resource group if it doesn't exist
Write-Host "`nCreating resource group: $ResourceGroup..." -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --output none

# Prompt for secrets
$sqlPassword = Read-Host "Enter SQL Admin Password" -AsSecureString
$finnhubKey = Read-Host "Enter Finnhub API Key" -AsSecureString

# Convert secure strings to plain text for Azure CLI
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sqlPassword)
$sqlPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)

$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($finnhubKey)
$finnhubKeyPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)

# Deploy Bicep template
Write-Host "`nDeploying infrastructure..." -ForegroundColor Cyan
$deployResult = az deployment group create `
    --resource-group $ResourceGroup `
    --template-file "$PSScriptRoot\main.bicep" `
    --parameters environment=$Environment `
    --parameters location=$Location `
    --parameters sqlAdminPassword=$sqlPasswordPlain `
    --parameters finnhubApiKey=$finnhubKeyPlain `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Host "Deployment failed!" -ForegroundColor Red
    exit 1
}

# Clear sensitive data from memory
$sqlPasswordPlain = $null
$finnhubKeyPlain = $null
[System.GC]::Collect()

# Extract outputs
$appUrl = $deployResult.properties.outputs.appServiceUrl.value
$appName = $deployResult.properties.outputs.appServiceName.value
$sqlFqdn = $deployResult.properties.outputs.sqlServerFqdn.value

Write-Host "`n=== Deployment Complete ===" -ForegroundColor Green
Write-Host "App Service URL: $appUrl" -ForegroundColor Cyan
Write-Host "App Service Name: $appName" -ForegroundColor Cyan
Write-Host "SQL Server: $sqlFqdn" -ForegroundColor Cyan

# Run database migrations
Write-Host "`nTo apply database migrations, run:" -ForegroundColor Yellow
Write-Host "  dotnet ef database update --project src/StockAnalyzer.Core --startup-project src/StockAnalyzer.Api --connection `"<connection-string>`"" -ForegroundColor White

Write-Host "`nTo deploy your container image:" -ForegroundColor Yellow
Write-Host "  1. Build: docker build -t ghcr.io/psford/stockanalyzer:latest ." -ForegroundColor White
Write-Host "  2. Push: docker push ghcr.io/psford/stockanalyzer:latest" -ForegroundColor White
Write-Host "  3. Restart: az webapp restart --name $appName --resource-group $ResourceGroup" -ForegroundColor White

Write-Host "`n=== Done ===" -ForegroundColor Green
