// Storage account + Static Web App, and the app settings that join them together.

param location string
param staticWebAppLocation string
param resourceToken string
param tags object

@secure()
param sharedPassword string

@secure()
param authSigningKey string

param sessionDays int

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'stbunkers${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

// The API creates the Bunkers and Nights tables on first use, so there's nothing to
// declare here beyond the table service itself.
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-01-01' = {
  parent: storage
  name: 'default'
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-01-01' = {
  name: 'swa-bunkers-${resourceToken}'
  location: staticWebAppLocation
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Deployment comes from the GitHub Actions workflow using the deployment token,
    // not from a repository link managed by Azure.
    provider: 'Custom'
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Enabled'
  }
}

// These land as environment variables on the managed function app. The API reads all
// four at startup and throws if any are missing.
resource staticWebAppSettings 'Microsoft.Web/staticSites/config@2023-01-01' = {
  parent: staticWebApp
  name: 'appsettings'
  properties: {
    TableStorageConnectionString: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
    SharedPassword: sharedPassword
    AuthSigningKey: authSigningKey
    SessionDays: string(sessionDays)
  }
}

output storageAccountName string = storage.name
output staticWebAppName string = staticWebApp.name
output staticWebAppUri string = 'https://${staticWebApp.properties.defaultHostname}'
