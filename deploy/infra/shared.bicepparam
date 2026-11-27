using './shared.bicep'

param location = readEnvironmentVariable('PCS_LOCATION')
param sharedResourceGroup = readEnvironmentVariable('PCS_SHARED_RESOURCE_GROUP')
param appServicePlanName = readEnvironmentVariable('PCS_APP_SERVICE_PLAN_NAME')
param sqlServerName = readEnvironmentVariable('PCS_SQL_SERVER_NAME')
param sqlAdminGroupObjectId = readEnvironmentVariable('PCS_SQL_ADMIN_GROUP_OBJECT_ID')
param sqlAdminGroupName = readEnvironmentVariable('PCS_SQL_ADMIN_GROUP_NAME')
param appServicePlanSku = readEnvironmentVariable('PCS_APP_SERVICE_PLAN_SKU', 'F1')
