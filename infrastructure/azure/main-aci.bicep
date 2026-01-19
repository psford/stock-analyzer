// Azure Infrastructure for Stock Analyzer - Container Instances Version
// Deploy with: az deployment group create -g rg-stockanalyzer-prod -f main-aci.bicep
//
// This template uses GHCR (GitHub Container Registry) for images.
// Before deploying, ensure the image exists at ghcr.io/psford/stockanalyzer:latest
// by pushing to GitHub and running the GitHub Actions workflow.

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

@description('Container image to deploy')
param containerImage string = 'ghcr.io/psford/stockanalyzer:latest'

@description('GitHub username for GHCR authentication')
param ghcrUsername string = 'psford'

@description('GitHub token (PAT) for GHCR authentication')
@secure()
param ghcrToken string = ''

// Resource naming
var appName = 'stockanalyzer'
var shortSuffix = substring(uniqueString(resourceGroup().id), 0, 6)
var sqlServerName = 'sql-${appName}-${shortSuffix}'
var sqlDatabaseName = '${appName}db'
var containerGroupName = 'aci-${appName}'

// SQL Server (if not already exists)
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

// SQL Database (Basic tier - 5 DTU, ~$5/mo)
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
  }
}

// SQL Firewall - Allow Azure services
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Connection string for the container
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};User ID=${sqlAdminUsername};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'

// Azure Container Instance - pulls from GHCR
resource containerGroup 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = {
  name: containerGroupName
  location: location
  properties: {
    containers: [
      {
        name: appName
        properties: {
          image: containerImage
          ports: [
            {
              port: 5000
              protocol: 'TCP'
            }
          ]
          environmentVariables: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:5000'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              secureValue: sqlConnectionString
            }
            {
              name: 'Finnhub__ApiKey'
              secureValue: finnhubApiKey
            }
          ]
          resources: {
            requests: {
              cpu: 1
              memoryInGB: 2
            }
          }
        }
      }
    ]
    osType: 'Linux'
    restartPolicy: 'Always'
    ipAddress: {
      type: 'Public'
      ports: [
        {
          port: 5000
          protocol: 'TCP'
        }
      ]
      dnsNameLabel: '${appName}-${shortSuffix}'
    }
    // Only include GHCR credentials if token is provided
    imageRegistryCredentials: empty(ghcrToken) ? [] : [
      {
        server: 'ghcr.io'
        username: ghcrUsername
        password: ghcrToken
      }
    ]
  }
  dependsOn: [
    sqlDatabase
  ]
}

// Outputs
output containerUrl string = 'http://${containerGroup.properties.ipAddress.fqdn}:5000'
output containerIp string = containerGroup.properties.ipAddress.ip
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output dnsLabel string = '${appName}-${shortSuffix}'
