[CmdletBinding()]
param([Parameter(Mandatory)][string]$MigrationScript)
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

Assert-DeploymentAccount
Import-Module SqlServer -RequiredVersion 22.4.5.1
$hostname = Invoke-AzureCli @(
    'sql', 'server', 'show', '--resource-group', (Get-DeploymentVariable PCS_SHARED_RESOURCE_GROUP),
    '--name', (Get-DeploymentVariable PCS_SQL_SERVER_NAME),
    '--query', 'fullyQualifiedDomainName', '-o', 'tsv'
)
Register-DeploymentSecret $hostname
$identity = Invoke-AzureCli @(
    'identity', 'show', '--resource-group', (Get-DeploymentVariable PCS_APP_RESOURCE_GROUP),
    '--name', "$(Get-DeploymentVariable PCS_API_APP_NAME)-db", '-o', 'json'
) | ConvertFrom-Json
Register-DeploymentSecret $identity.clientId
Register-DeploymentSecret $identity.principalId
$token = Invoke-AzureCli @('account', 'get-access-token', '--resource', 'https://database.windows.net/',
    '--query', 'accessToken', '-o', 'tsv')
Register-DeploymentSecret $token
$sql = @{
    ServerInstance = $hostname
    Database = Get-DeploymentVariable PCS_SQL_DATABASE_NAME
    AccessToken = $token
    ConnectionTimeout = 120
    QueryTimeout = 300
    AbortOnError = $true
    DisableVariables = $true
    DisableCommands = $true
    ErrorAction = 'Stop'
}
Invoke-Sqlcmd @sql -InputFile $MigrationScript | Out-Null
Write-Host 'Database migrations applied.'

$sid = '0x' + [Convert]::ToHexString(([guid]$identity.clientId).ToByteArray())
$user = $identity.name.Replace(']', ']]')
$literal = $identity.name.Replace("'", "''")
# TYPE=E and an explicit SID avoid Graph directory lookups for CREATE USER.
$grant = @"
IF DATABASE_PRINCIPAL_ID(N'$literal') IS NULL
    CREATE USER [$user] WITH SID = $sid, TYPE = E;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literal' AND sid = $sid AND type = 'E')
    THROW 51000, 'The existing API database principal does not match its managed identity.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID('db_datareader')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'$literal'))
    ALTER ROLE db_datareader ADD MEMBER [$user];
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID('db_datawriter')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'$literal'))
    ALTER ROLE db_datawriter ADD MEMBER [$user];
"@
Invoke-Sqlcmd @sql -Query $grant | Out-Null
Write-Host 'API database permissions verified.'
