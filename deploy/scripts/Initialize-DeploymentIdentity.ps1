[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$TenantId,
    [Parameter(Mandatory)][string]$SubscriptionId,
    [Parameter(Mandatory)][string]$PipelineAppName,
    [Parameter(Mandatory)][string]$SqlAdminGroupName,
    [Parameter(Mandatory)][guid]$AdministratorObjectId,
    [Parameter(Mandatory)][string]$FederationIssuer,
    [Parameter(Mandatory)][string]$FederationSubject
)
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

$account = Invoke-AzureCli @('account', 'show', '-o', 'json') | ConvertFrom-Json
if ($account.tenantId -ne $TenantId -or $account.id -ne $SubscriptionId) {
    throw 'Sign into the intended tenant and select the intended subscription before bootstrapping.'
}
if (-not $PSCmdlet.ShouldProcess('Configured tenant and subscription',
    'Create the dedicated pipeline identity, SQL administrator group and permission grants')) { return }

$apps = @(Invoke-AzureCli @('ad', 'app', 'list', '--display-name', $PipelineAppName, '-o', 'json') |
    ConvertFrom-Json | Where-Object displayName -eq $PipelineAppName)
if ($apps.Count -gt 1) { throw 'Multiple applications have the configured pipeline name; resolve the ambiguity first.' }
if ($apps.Count -eq 0) {
    $app = Invoke-AzureCli @('ad', 'app', 'create', '--display-name', $PipelineAppName, '-o', 'json') | ConvertFrom-Json
}
else { $app = $apps[0] }
Register-DeploymentSecret $app.id
Register-DeploymentSecret $app.appId
$null = Invoke-AzureCli @('ad', 'app', 'update', '--id', $app.id, '--set', 'groupMembershipClaims=SecurityGroup')
$principals = @(Invoke-AzureCli @('ad', 'sp', 'list', '--display-name', $PipelineAppName, '-o', 'json') |
    ConvertFrom-Json | Where-Object appId -eq $app.appId)
if ($principals.Count -eq 0) {
    $principal = Invoke-AzureCli @('ad', 'sp', 'create', '--id', $app.appId, '-o', 'json') | ConvertFrom-Json
}
else { $principal = $principals[0] }
Register-DeploymentSecret $principal.id

$credentials = @(Invoke-AzureCli @('ad', 'app', 'federated-credential', 'list', '--id', $app.id, '-o', 'json') |
    ConvertFrom-Json | Where-Object {
        $_.name -eq 'azure-pipelines' -or
        ($_.issuer -eq $FederationIssuer -and $_.subject -eq $FederationSubject)
    })
if ($credentials.Count -gt 1) { throw 'Multiple matching federation credentials exist; resolve the ambiguity first.' }
$temporary = New-TemporaryFile
try {
    @{
        name = if ($credentials.Count -eq 0) { 'azure-pipelines' } else { $credentials[0].name }
        issuer = $FederationIssuer
        subject = $FederationSubject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json | Set-Content $temporary.FullName -Encoding utf8
    $args = @('ad', 'app', 'federated-credential')
    if ($credentials.Count -eq 0) { $args += 'create' }
    else { $args += @('update', '--federated-credential-id', $credentials[0].id) }
    $null = Invoke-AzureCli ($args + @('--id', $app.id, '--parameters', "@$($temporary.FullName)", '-o', 'none'))
}
finally { Remove-Item -LiteralPath $temporary.FullName }

$scope = "/subscriptions/$SubscriptionId"
$assignments = @(Invoke-AzureCli @('role', 'assignment', 'list', '--assignee-object-id', $principal.id,
    '--scope', $scope, '-o', 'json') | ConvertFrom-Json | Where-Object roleDefinitionName -eq 'Contributor')
if ($assignments.Count -eq 0) {
    $null = Invoke-AzureCli @('role', 'assignment', 'create', '--assignee-object-id', $principal.id,
        '--assignee-principal-type', 'ServicePrincipal', '--role', 'Contributor', '--scope', $scope, '-o', 'none')
}

$graph = @(Invoke-AzureCli @('ad', 'sp', 'list', '--display-name', 'Microsoft Graph', '-o', 'json') |
    ConvertFrom-Json | Where-Object displayName -eq 'Microsoft Graph')
if ($graph.Count -ne 1) { throw 'The Microsoft Graph service principal could not be resolved unambiguously.' }
$permission = $graph[0].appRoles | Where-Object value -eq 'Application.ReadWrite.OwnedBy'
$null = Invoke-AzureCli @('ad', 'app', 'permission', 'add', '--id', $app.id, '--api', $graph[0].appId,
    '--api-permissions', "$($permission.id)=Role")
$null = Invoke-AzureCli @('ad', 'app', 'permission', 'admin-consent', '--id', $app.id)

$groups = @(Invoke-AzureCli @('ad', 'group', 'list', '--display-name', $SqlAdminGroupName, '-o', 'json') |
    ConvertFrom-Json | Where-Object displayName -eq $SqlAdminGroupName)
if ($groups.Count -gt 1) { throw 'Multiple SQL administrator groups have this name; resolve the ambiguity first.' }
if ($groups.Count -eq 0) {
    $group = Invoke-AzureCli @('ad', 'group', 'create', '--display-name', $SqlAdminGroupName,
        '--mail-nickname', $SqlAdminGroupName, '-o', 'json') | ConvertFrom-Json
}
else { $group = $groups[0] }
foreach ($member in @($AdministratorObjectId.ToString(), $principal.id)) {
    $exists = Invoke-AzureCli @('ad', 'group', 'member', 'check', '--group', $group.id, '--member-id', $member,
        '--query', 'value', '-o', 'tsv')
    if ($exists -ne 'true') {
        $null = Invoke-AzureCli @('ad', 'group', 'member', 'add', '--group', $group.id, '--member-id', $member)
    }
}

Write-Host 'Dedicated deployment identity and SQL administrator group configured.'
# Returned only to the local administrator, never published to pipeline artifacts.
[pscustomobject]@{
    PipelineClientId = $app.appId
    PipelineObjectId = $principal.id
    SqlAdminGroupObjectId = $group.id
}
