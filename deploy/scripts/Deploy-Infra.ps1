[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InfraDirectory,
    [Parameter(Mandatory)][string]$DeploymentName
)
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

Assert-DeploymentAccount
$freeLimit = [bool]::Parse((Get-DeploymentVariable PCS_SQL_USE_FREE_LIMIT))
if ($freeLimit -and -not (Get-DeploymentVariable PCS_SQL_SKU_NAME).StartsWith('GP_S_', [StringComparison]::Ordinal)) {
    throw 'The free SQL allowance requires a General Purpose serverless SKU; explicitly disable it for other SKUs.'
}
# Optional: an unset variable-group entry arrives as an unresolved '$(...)' reference.
$env:PCS_WEB_CUSTOM_DOMAIN = (Get-OptionalDeploymentVariable PCS_WEB_CUSTOM_DOMAIN).ToLowerInvariant()
if ($env:PCS_WEB_CUSTOM_DOMAIN -and $env:PCS_WEB_CUSTOM_DOMAIN -notmatch '^(?!www\.)([a-z0-9-]+\.)+[a-z]{2,}$') {
    throw 'PCS_WEB_CUSTOM_DOMAIN must be an apex domain name such as example.com, without scheme or www.'
}

foreach ($template in 'shared', 'app') {
    # Graph objects created by the app template replicate asynchronously, so dependent
    # Graph operations can briefly fail; the deployment is idempotent and safe to repeat.
    $maxAttempts = 4
    for ($attempt = 1; ; $attempt++) {
        try {
            $null = Invoke-AzureCli @(
                'deployment', 'sub', 'create',
                '--name', "$DeploymentName-$template",
                '--location', (Get-DeploymentVariable PCS_LOCATION),
                '--parameters', (Join-Path $InfraDirectory "$template.bicepparam"),
                '-o', 'none'
            )
            break
        }
        catch {
            $transient = $_.Exception.Message -match 'Request_ResourceNotFound|cannot be found|local tenant'
            if (-not $transient -or $attempt -ge $maxAttempts) { throw }
            Write-Warning "$template deployment hit Graph replication delay (attempt $attempt of $maxAttempts); retrying."
            Start-Sleep -Seconds 60
        }
    }
    Write-Host "$template infrastructure deployed."
}
