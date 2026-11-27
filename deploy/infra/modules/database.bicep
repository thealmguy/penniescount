@secure()
param location string
@secure()
param sqlServerName string
@secure()
param sqlDatabaseName string
param sqlSkuName string
param sqlUseFreeLimit bool
param sqlAutoPauseDelay int
param sqlMaxSizeBytes int
param sqlMinCapacity string
var serverless = contains(sqlSkuName, '_S_')

resource server 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: sqlServerName
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: sqlDatabaseName
  location: location
  tags: { project: 'PenniesCount' }
  sku: {
    name: sqlSkuName
  }
  properties: union({
    maxSizeBytes: sqlMaxSizeBytes
    requestedBackupStorageRedundancy: 'Local'
  }, serverless ? {
    autoPauseDelay: sqlAutoPauseDelay
    minCapacity: json(sqlMinCapacity)
  } : {}, {
    useFreeLimit: sqlUseFreeLimit
    freeLimitExhaustionBehavior: sqlUseFreeLimit ? 'AutoPause' : 'BillOverUsage'
  })
}

@secure()
output hostname string = server.properties.fullyQualifiedDomainName
