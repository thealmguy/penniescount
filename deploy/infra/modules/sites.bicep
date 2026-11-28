@secure()
param location string
@secure()
param planId string
@secure()
param apiAppName string
@secure()
param webAppName string
param alwaysOn bool

resource databaseIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${apiAppName}-db'
  location: location
}

resource api 'Microsoft.Web/sites@2024-11-01' = {
  name: apiAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${databaseIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: planId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
    }
  }
}

resource web 'Microsoft.Web/sites@2024-11-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: planId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      webSocketsEnabled: true
    }
  }
}

resource apiFtp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: api
  name: 'ftp'
  properties: { allow: false }
}

resource webFtp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: web
  name: 'ftp'
  properties: { allow: false }
}

@secure()
output details object = {
  apiHostname: api.properties.defaultHostName
  webHostname: web.properties.defaultHostName
  webPrincipalId: web.identity.principalId
  databaseIdentityName: databaseIdentity.name
  databaseClientId: databaseIdentity.properties.clientId
}
