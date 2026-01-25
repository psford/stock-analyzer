// Azure Infrastructure for Stock Analyzer
// Deploy with: az deployment group create -g rg-stockanalyzer-prod -f main.bicep -p parameters.json

@description('Environment name (prod, dev, staging)')
param environment string = 'prod'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('SQL Server admin username')
param sqlAdminUsername string = 'sqladmin'

@description('SQL Server admin password')
@secure()
param sqlAdminPassword string

@description('Finnhub API key')
@secure()
param finnhubApiKey string

@description('EODHD API key')
@secure()
param eodhdApiKey string

// Resource naming - shortened for Azure constraints
var appName = 'stockanalyzer'
var shortSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
var appServicePlanName = 'asp-${appName}'
var appServiceName = 'app-${appName}-${shortSuffix}'
var sqlServerName = 'sql-${appName}-${shortSuffix}'
// IMPORTANT: This database contains pre-loaded BACPAC data (3.5M+ price records).
// DO NOT change this name or recreate the database - it would destroy production data.
var sqlDatabaseName = 'stockanalyzer-db'
var keyVaultName = 'kv-stk-${shortSuffix}' // Must be 3-24 chars

// App Service Plan (Linux, F1 Free tier for quota limits)
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
    capacity: 1
  }
  properties: {
    reserved: true // Required for Linux
  }
}

// App Service (Docker container)
resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOCKER|ghcr.io/psford/stockanalyzer:latest'
      alwaysOn: false // F1 tier doesn't support alwaysOn
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'WEBSITES_PORT'
          value: '5000'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'Finnhub__ApiKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=FinnhubApiKey)'
        }
        {
          name: 'Eodhd__ApiKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=EodhdApiKey)'
        }
      ]
      connectionStrings: [
        {
          name: 'DefaultConnection'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};User ID=${sqlAdminUsername};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'
          type: 'SQLAzure'
        }
      ]
    }
    httpsOnly: true
  }
}

// SQL Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminUsername
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// SQL Database - NOT MANAGED BY BICEP
// The database 'stockanalyzer-db' was created via BACPAC import and contains 3.5M+ price records.
// DO NOT add a database resource here - it would recreate/overwrite production data.
// If you need to recreate infrastructure, export the database to BACPAC first!

// SQL Firewall - Allow Azure services
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Key Vault for secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Store Finnhub API key in Key Vault
resource finnhubSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'FinnhubApiKey'
  properties: {
    value: finnhubApiKey
  }
}

// Store EODHD API key in Key Vault
resource eodhdSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'EodhdApiKey'
  properties: {
    value: eodhdApiKey
  }
}

// Grant App Service access to Key Vault
resource keyVaultAccessPolicy 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appService.id, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output appServiceName string = appService.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output keyVaultName string = keyVault.name
