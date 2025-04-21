// storage.bicep

param location string
param storageAccountName string
param cacheRetentionDays int

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}

resource cachedFilesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-09-01' = {
  name: '${storageAccount.name}/default/cached-files'
}

resource permanentFilesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-09-01' = {
  name: '${storageAccount.name}/default/permanent-files'
}

resource userUploadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-09-01' = {
  name: '${storageAccount.name}/default/user-uploads'
}

resource storageManagementPolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2021-04-01' = {
  name: 'default'
  parent: storageAccount
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'DeleteCachedFiles'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterLastAccessTimeGreaterThan: cacheRetentionDays
                }
              }
            }
            filters: {
              blobTypes: [ 'blockBlob' ]
              prefixMatch: [ 'cached-files/' ]
            }
          }
        }
      ]
    }
  }
}

output storageAccountName string = storageAccount.name
