[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InfraDirectory,
    [Parameter(Mandatory)][string]$DeploymentName
)
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

Assert-DeploymentAccount
$users = Get-DeploymentVariable PCS_ALLOWED_USER_OBJECT_IDS | ConvertFrom-Json -NoEnumerate
if ($users -isnot [array] -or $users.Count -eq 0) {
    throw 'PCS_ALLOWED_USER_OBJECT_IDS must be a nonempty JSON array of user object IDs.'
}
foreach ($userId in $users) { $null = [guid]::Parse($userId) }
$freeLimit = [bool]::Parse((Get-DeploymentVariable PCS_SQL_USE_FREE_LIMIT))
if ($freeLimit -and -not (Get-DeploymentVariable PCS_SQL_SKU_NAME).StartsWith('GP_S_', [StringComparison]::Ordinal)) {
    throw 'The free SQL allowance requires a General Purpose serverless SKU; explicitly disable it for other SKUs.'
}

foreach ($template in 'shared', 'app') {
    $null = Invoke-AzureCli @(
        'deployment', 'sub', 'create',
        '--name', "$DeploymentName-$template",
        '--location', (Get-DeploymentVariable PCS_LOCATION),
        '--parameters', (Join-Path $InfraDirectory "$template.bicepparam"),
        '-o', 'none'
    )
    Write-Host "$template infrastructure deployed."
}
