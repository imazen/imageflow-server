// keyVault.bicep

param location string
param resourceTags object
param secrets array

resource keyVault 'Microsoft.KeyVault/vaults@2022-07-01' = {
  name: 'kv-${uniqueString(resourceGroup().id)}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
    accessPolicies: [] // Policies will be added after identity creation
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: true
  }
  tags: resourceTags
}

resource keyVaultSecrets 'Microsoft.KeyVault/vaults/secrets@2022-07-01' = [for secret in secrets: {
  parent: keyVault
  name: secret.name
  properties: {
    value: secret.value
  }
}]

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
