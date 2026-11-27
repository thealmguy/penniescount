@secure()
param apiAppName string
@secure()
param webAppName string
@secure()
param siteDetails object
@secure()
param entraDetails object
@secure()
param sqlHostname string
@secure()
param sqlDatabaseName string
var common = {
  ASPNETCORE_ENVIRONMENT: 'Production'
  ASPNETCORE_FORWARDEDHEADERS_ENABLED: 'true'
  WEBSITE_RUN_FROM_PACKAGE: '1'
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
  AzureAd__Instance: environment().authentication.loginEndpoint
  AzureAd__TenantId: tenant().tenantId
}

resource api 'Microsoft.Web/sites@2024-11-01' existing = { name: apiAppName }
resource web 'Microsoft.Web/sites@2024-11-01' existing = { name: webAppName }

resource apiSettings 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: api
  name: 'appsettings'
  properties: union(common, {
    AllowedHosts: siteDetails.apiHostname
    AzureAd__ClientId: entraDetails.apiClientId
    AuthorizedClientId: entraDetails.webClientId
    ConnectionStrings__Savings: 'Server=tcp:${sqlHostname};Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${siteDetails.databaseClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=120;ConnectRetryCount=3;ConnectRetryInterval=10'
  })
}

resource webSettings 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: web
  name: 'appsettings'
  properties: union(common, {
    AllowedHosts: siteDetails.webHostname
    AzureAd__ClientId: entraDetails.webClientId
    AzureAd__CallbackPath: '/signin-oidc'
    AzureAd__ClientCredentials__0__SourceType: 'SignedAssertionFromManagedIdentity'
    ApiScope: entraDetails.apiScope
    ApiBaseUrl: 'https://${siteDetails.apiHostname}/'
  })
}
