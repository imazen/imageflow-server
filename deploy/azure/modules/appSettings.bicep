// appSettings.bicep (optional module for complex app settings management)

// This module can be used if you have complex logic for app settings.

param appSettings array

resource appServiceAppSettings 'Microsoft.Web/sites/config@2022-03-01' = {
  name: '${webAppName}/appsettings'
  properties: {
    for setting in appSettings: {
      setting.name: setting.value
    }
  }
  dependsOn: [
    webApp
  ]
}
