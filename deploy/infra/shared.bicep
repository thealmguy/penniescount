targetScope = 'subscription'

@secure()
param location string
@secure()
param sharedResourceGroup string
@secure()
param appServicePlanName string
@secure()
param sqlServerName string
@secure()
param sqlAdminGroupObjectId string
@secure()
param sqlAdminGroupName string
param appServicePlanSku string = 'F1'

resource shared 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: sharedResourceGroup
  location: location
}

module resources 'modules/shared-resources.bicep' = {
  name: 'shared-resources'
  scope: shared
  params: {
    location: location
    appServicePlanName: appServicePlanName
    appServicePlanSku: appServicePlanSku
    sqlServerName: sqlServerName
    sqlAdminGroupObjectId: sqlAdminGroupObjectId
    sqlAdminGroupName: sqlAdminGroupName
  }
}
