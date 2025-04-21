// appService.bicep

param location string
param dockerImageName string
param dockerImageTag string
param storageAccountName string
param appSettings array
param routeMappings string
param customDomainName string
param enableCdn bool
param keyVaultName string
param appServiceIdentityType string
param resourceTags object

var appServicePlanName = 'asp-${uniqueString(resourceGroup().id)}'
var webAppName = 'webapp-${uniqueString(resourceGroup().id)}'

resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: 'P1v2'
    tier: 'PremiumV2'
  }
  kind: 'linux'
  tags: resourceTags
}

resource webApp 'Microsoft.Web/sites@2022-03-01' = {
  name: webAppName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: appServiceIdentityType
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOCKER|${dockerImageName}:${dockerImageTag}'
      appSettings: [
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_URL'
          value: ''
        }
        // Include additional app settings from parameter
        for setting in appSettings: {
          name: setting.name
          value: setting.value
        }
        // Reference Key Vault secrets if any
        {
          name: 'KeyVaultUri'
          value: 'https://${keyVaultName}.vault.azure.net/'
        }
        // Route mappings as an app setting
        {
          name: 'ROUTE_MAPPINGS'
          value: routeMappings
        }
      ]
    }
  }
  tags: resourceTags
  dependsOn: [
    appServicePlan
  ]
}

resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2022-07-01' = {
  name: '${keyVaultName}/add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: webApp.identity.principalId
        permissions: {
          secrets: [ 'get', 'list' ]
        }
      }
    ]
  }
  dependsOn: [
    webApp
  ]
}

resource cdnProfile 'Microsoft.Cdn/profiles@2022-09-01' = if (enableCdn) {
  name: 'cdn-${uniqueString(resourceGroup().id)}'
  location: 'global'
  sku: {
    name: 'Standard_Microsoft'
  }
  tags: resourceTags
}

resource cdnEndpoint 'Microsoft.Cdn/profiles/endpoints@2022-09-01' = if (enableCdn) {
  name: '${cdnProfile.name}/endpoint'
  properties: {
    origins: [
      {
        name: 'origin'
        properties: {
          hostName: webApp.properties.defaultHostName
        }
      }
    ]
    isHttpAllowed: true
    isHttpsAllowed: true
  }
  dependsOn: [
    cdnProfile
    webApp
  ]
}

resource customDomain 'Microsoft.Web/sites/hostNameBindings@2022-03-01' = if (!empty(customDomainName)) {
  name: '${webApp.name}/${customDomainName}'
  properties: {
    hostNameType: 'Verified'
  }
  dependsOn: [
    webApp
  ]
}

output webAppDefaultHostName string = webApp.properties.defaultHostName
