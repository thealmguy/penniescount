targetScope = 'subscription'

@secure()
param location string
@secure()
param appResourceGroup string
@secure()
param sharedResourceGroup string
@secure()
param appServicePlanName string
@secure()
param sqlServerName string
@secure()
param sqlDatabaseName string
@secure()
param apiAppName string
@secure()
param webAppName string
@secure()
param entraAppNamePrefix string
@description('Optional apex custom domain for the Web app; empty serves only the default hostname.')
param webCustomDomain string = ''
param appServicePlanSku string = 'F1'
param sqlSkuName string = 'GP_S_Gen5_2'
param sqlUseFreeLimit bool = true
param sqlAutoPauseDelay int = 60
param sqlMaxSizeBytes int = 34359738368
param sqlMinCapacity string = '0.5'
// Free (F) and Shared (D) plans do not support Always On.
var alwaysOn = !startsWith(toUpper(appServicePlanSku), 'F') && !startsWith(toUpper(appServicePlanSku), 'D')

resource apps 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: appResourceGroup
  location: location
  tags: {
    project: 'PenniesCount'
  }
}

module sites 'modules/sites.bicep' = {
  name: 'sites'
  scope: apps
  params: {
    location: location
    planId: resourceId(subscription().subscriptionId, sharedResourceGroup, 'Microsoft.Web/serverfarms', appServicePlanName)
    apiAppName: apiAppName
    webAppName: webAppName
    alwaysOn: alwaysOn
  }
}

module database 'modules/database.bicep' = {
  name: 'database'
  scope: resourceGroup(sharedResourceGroup)
  params: {
    location: location
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDatabaseName
    sqlSkuName: sqlSkuName
    sqlUseFreeLimit: sqlUseFreeLimit
    sqlAutoPauseDelay: sqlAutoPauseDelay
    sqlMaxSizeBytes: sqlMaxSizeBytes
    sqlMinCapacity: sqlMinCapacity
  }
}

module entra 'modules/entra.bicep' = {
  name: 'entra'
  scope: apps
  params: {
    namePrefix: entraAppNamePrefix
    webHostname: empty(webCustomDomain) ? sites.outputs.details.webHostname : webCustomDomain
    webPrincipalId: sites.outputs.details.webPrincipalId
  }
}

module settings 'modules/settings.bicep' = {
  name: 'settings'
  scope: apps
  params: {
    apiAppName: apiAppName
    webAppName: webAppName
    siteDetails: sites.outputs.details
    entraDetails: entra.outputs.details
    sqlHostname: database.outputs.hostname
    sqlDatabaseName: sqlDatabaseName
    webCustomDomain: webCustomDomain
  }
}
