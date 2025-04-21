// functionApp.bicep

param location string
param functionAppPackageUrl string
param storageAccountName string
param functionAppIdentityType string
param keyVaultName string
param resourceTags object

var functionAppName = 'func-${uniqueString(resourceGroup().id)}'

resource functionStorage 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: 'stfunc${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  tags: resourceTags
}

resource functionAppPlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: 'functionAppPlan-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  tags: resourceTags
}

resource functionApp 'Microsoft.Web/sites@2022-03-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: functionAppIdentityType
  }
  properties: {
    serverFarmId: functionAppPlan.id
    siteConfig: {
      appSettings: [
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet'
        }
        {
          name: 'AzureWebJobsStorage'
          value: functionStorage.properties.primaryConnectionString
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: functionAppPackageUrl
        }
        {
          name: 'KeyVaultUri'
          value: 'https://${keyVaultName}.vault.azure.net/'
        }
      ]
    }
  }
  dependsOn: [
    functionAppPlan
    functionStorage
  ]
  tags: resourceTags
}

resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2022-07-01' = {
  name: '${keyVaultName}/addFunctionApp'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [ 'get', 'list' ]
        }
      }
    ]
  }
  dependsOn: [
    functionApp
  ]
}

output functionAppDefaultHostName string = functionApp.properties.defaultHostName
