[CmdletBinding()]
param()
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

$domain = (Get-OptionalDeploymentVariable PCS_WEB_CUSTOM_DOMAIN).ToLowerInvariant()
if (-not $domain) {
    Write-Host 'No Web custom domain configured; skipping hostname and certificate binding.'
    return
}
Assert-DeploymentAccount
$group = Get-DeploymentVariable PCS_APP_RESOURCE_GROUP
$app = Get-DeploymentVariable PCS_WEB_APP_NAME

function Get-Property {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $null
}

function Get-ManagedCertificate {
    param([string]$Hostname)
    $certificates = Invoke-AzureCli @('webapp', 'config', 'ssl', 'list', '--resource-group', $group, '-o', 'json') |
        ConvertFrom-Json
    return @($certificates | Where-Object {
        (Get-Property $_ canonicalName) -eq $Hostname -and (Get-Property $_ thumbprint) -and
        [datetime](Get-Property $_ expirationDate) -gt (Get-Date).AddDays(7)
    }) | Select-Object -First 1
}

# Hostname bindings and certificates are managed here rather than in Bicep, because a
# redeployed SNI binding would otherwise be reset and briefly serve HTTPS without it.
foreach ($entry in @(@{ Label = 'apex'; Hostname = $domain }, @{ Label = 'www'; Hostname = "www.$domain" })) {
    $hostname = $entry.Hostname
    $label = $entry.Label
    $bindings = Invoke-AzureCli @('webapp', 'config', 'hostname', 'list', '--resource-group', $group,
        '--webapp-name', $app, '-o', 'json') | ConvertFrom-Json
    $binding = @($bindings | Where-Object name -eq $hostname) | Select-Object -First 1
    if (-not $binding) {
        # Fails until the domain's DNS records and asuid TXT verification records exist.
        $null = Invoke-AzureCli @('webapp', 'config', 'hostname', 'add', '--resource-group', $group,
            '--webapp-name', $app, '--hostname', $hostname, '-o', 'none')
        Write-Host "Bound the $label hostname."
    }

    $certificate = Get-ManagedCertificate $hostname
    if (-not $certificate) {
        $null = Invoke-AzureCli @('webapp', 'config', 'ssl', 'create', '--resource-group', $group,
            '--name', $app, '--hostname', $hostname, '-o', 'none')
        $deadline = (Get-Date).AddMinutes(15)
        while (-not ($certificate = Get-ManagedCertificate $hostname)) {
            if ((Get-Date) -gt $deadline) { throw "The $label managed certificate was not issued in time." }
            Start-Sleep -Seconds 20
        }
        Write-Host "Issued the $label managed certificate."
    }
    Register-DeploymentSecret $certificate.thumbprint

    if ((Get-Property $binding sslState) -ne 'SniEnabled' -or (Get-Property $binding thumbprint) -ne $certificate.thumbprint) {
        $null = Invoke-AzureCli @('webapp', 'config', 'ssl', 'bind', '--resource-group', $group,
            '--name', $app, '--hostname', $hostname, '--certificate-thumbprint', $certificate.thumbprint,
            '--ssl-type', 'SNI', '-o', 'none')
        Write-Host "Enabled SNI TLS for the $label hostname."
    }
}
Write-Host 'Web custom domain bindings verified.'
