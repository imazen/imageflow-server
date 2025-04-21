// main.bicep

@description('Location for all resources')
param location string = resourceGroup().location

@description('Docker image name')
param dockerImageName string = 'imazen/imageflow-server'

@description('Docker image tag or version')
param dockerImageTag string = 'latest'

@description('URL of the Azure Function App package')
param functionAppPackageUrl string = ''

@description('Storage account name')
param storageAccountName string = 'st${uniqueString(resourceGroup().id)}'

@description('Number of days before deleting unused blobs in the cached files container')
param cacheRetentionDays int = 30

@description('Custom domain name for the App Service (optional)')
param customDomainName string = ''

@description('Enable CDN in front of the App Service')
param enableCdn bool = false

@description('Application settings for the App Service')
param appSettings array = []

@description('Route mappings configuration (TOML format)')
param routeMappings string = ''

@description('Secrets to store in Key Vault')
param secrets array = []

@description('Managed identity type for App Service')
param appServiceIdentityType string = 'SystemAssigned'

@description('Managed identity type for Function App')
param functionAppIdentityType string = 'SystemAssigned'

@description('Resource tags')
param resourceTags object = {
  Environment: 'Production'
  Owner: 'YourName'
}

// Import modules
module storageModule './modules/storage.bicep' = {
  name: 'storageDeployment'
  params: {
    location: location
    storageAccountName: storageAccountName
    cacheRetentionDays: cacheRetentionDays
  }
}

module keyVaultModule './modules/keyVault.bicep' = {
  name: 'keyVaultDeployment'
  params: {
    location: location
    resourceTags: resourceTags
    secrets: secrets
  }
}

module appServiceModule './modules/appService.bicep' = {
  name: 'appServiceDeployment'
  params: {
    location: location
    dockerImageName: dockerImageName
    dockerImageTag: dockerImageTag
    storageAccountName: storageAccountName
    appSettings: appSettings
    routeMappings: routeMappings
    customDomainName: customDomainName
    enableCdn: enableCdn
    keyVaultName: keyVaultModule.outputs.keyVaultName
    appServiceIdentityType: appServiceIdentityType
    resourceTags: resourceTags
  }
  dependsOn: [
    storageModule
    keyVaultModule
  ]
}

module functionAppModule './modules/functionApp.bicep' = {
  name: 'functionAppDeployment'
  params: {
    location: location
    functionAppPackageUrl: functionAppPackageUrl
    storageAccountName: storageAccountName
    functionAppIdentityType: functionAppIdentityType
    keyVaultName: keyVaultModule.outputs.keyVaultName
    resourceTags: resourceTags
  }
  dependsOn: [
    storageModule
    keyVaultModule
  ]
}

output webAppUrl string = 'https://${appServiceModule.outputs.webAppDefaultHostName}'
output functionAppUrl string = 'https://${functionAppModule.outputs.functionAppDefaultHostName}'
