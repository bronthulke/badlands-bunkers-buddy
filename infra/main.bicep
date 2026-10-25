// Infrastructure for Badlands Bunkers Buddy.
//
//   azd up
//
// Creates a resource group, a storage account for the bunker tables, and a free-tier
// Static Web App wired with the four settings the API needs to boot.

targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment — used to derive resource names.')
param environmentName string

@minLength(1)
@description('Location for the storage account.')
param location string

@description('''
Location for the Static Web App. SWA with managed functions is only available in a
handful of regions, which usually differ from where you'd put the storage account.
This only affects the managed API — static content is served from the CDN edge.
''')
@allowed([
  'westus2'
  'centralus'
  'eastus2'
  'westeurope'
  'eastasia'
])
param staticWebAppLocation string = 'eastasia'

@secure()
@minLength(8)
@description('The password you share with your group.')
param sharedPassword string

@secure()
@minLength(32)
@description('''
Secret that signs session cookies. Changing it invalidates every existing session,
which is your "the password leaked" button. Generate with: openssl rand -base64 48
''')
param authSigningKey string

@minValue(1)
@maxValue(365)
@description('How many days a login lasts before it has to be re-entered.')
param sessionDays int = 30

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
  application: 'badlands-bunkers-buddy'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  name: 'resources'
  scope: resourceGroup
  params: {
    location: location
    staticWebAppLocation: staticWebAppLocation
    resourceToken: resourceToken
    tags: tags
    sharedPassword: sharedPassword
    authSigningKey: authSigningKey
    sessionDays: sessionDays
  }
}

output AZURE_LOCATION string = location
output AZURE_RESOURCE_GROUP string = resourceGroup.name
output AZURE_STATIC_WEB_APP_NAME string = resources.outputs.staticWebAppName
output AZURE_STORAGE_ACCOUNT_NAME string = resources.outputs.storageAccountName
output SERVICE_WEB_URI string = resources.outputs.staticWebAppUri
