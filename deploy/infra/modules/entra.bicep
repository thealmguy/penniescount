extension microsoftGraphV1

@secure()
param namePrefix string
@secure()
param webHostname string
@secure()
param webPrincipalId string

var apiIdentifier = 'api://${tenant().tenantId}/${namePrefix}-api'
var scopeId = guid(tenant().tenantId, namePrefix, 'access_as_user')

resource web 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: '${namePrefix}-web'
  displayName: '${namePrefix}-web'
  signInAudience: 'AzureADMyOrg'
  web: {
    redirectUris: [
      'https://${webHostname}/signin-oidc'
    ]
    logoutUrl: 'https://${webHostname}/signout-callback-oidc'
    implicitGrantSettings: {
      enableAccessTokenIssuance: false
      enableIdTokenIssuance: false
    }
  }
}

resource webFederation 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = {
  name: '${web.uniqueName}/app-service'
  issuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
  subject: webPrincipalId
  audiences: [
    'api://AzureADTokenExchange'
  ]
}

resource api 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: '${namePrefix}-api'
  displayName: '${namePrefix}-api'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    apiIdentifier
  ]
  api: {
    requestedAccessTokenVersion: 2
    oauth2PermissionScopes: [
      {
        id: scopeId
        value: 'access_as_user'
        type: 'Admin'
        isEnabled: true
        adminConsentDisplayName: 'Access PenniesCount'
        adminConsentDescription: 'Access the PenniesCount API as the signed-in user.'
      }
    ]
    preAuthorizedApplications: [
      {
        appId: web.appId
        delegatedPermissionIds: [
          scopeId
        ]
      }
    ]
  }
}

resource apiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: api.appId
}

resource webPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: web.appId
}

@secure()
output details object = {
  apiClientId: api.appId
  webClientId: web.appId
  apiScope: '${apiIdentifier}/access_as_user'
}
