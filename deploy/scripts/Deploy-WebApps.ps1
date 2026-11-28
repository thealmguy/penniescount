[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ApiPackage,
    [Parameter(Mandatory)][string]$WebPackage
)
. "$PSScriptRoot\Common.ps1"
trap { Write-Error (Protect-DeploymentMessage $_.Exception.Message) -ErrorAction Continue; exit 1 }

Assert-DeploymentAccount
foreach ($package in @(
    @{ Variable = 'PCS_API_APP_NAME'; File = $ApiPackage },
    @{ Variable = 'PCS_WEB_APP_NAME'; File = $WebPackage }
)) {
    $null = Invoke-AzureCli @(
        'webapp', 'deploy', '--resource-group', (Get-DeploymentVariable PCS_APP_RESOURCE_GROUP),
        '--name', (Get-DeploymentVariable $package.Variable),
        '--src-path', $package.File, '--type', 'zip', '--timeout', '600000', '-o', 'none'
    )
}
Write-Host 'API and Web packages deployed.'

function Get-Hostname {
    param([string]$Variable)
    $hostname = Invoke-AzureCli @(
        'webapp', 'show', '--resource-group', (Get-DeploymentVariable PCS_APP_RESOURCE_GROUP),
        '--name', (Get-DeploymentVariable $Variable), '--query', 'defaultHostName', '-o', 'tsv'
    )
    Register-DeploymentSecret $hostname
    return $hostname
}

$apiHostname = Get-Hostname PCS_API_APP_NAME
$webHostname = Get-Hostname PCS_WEB_APP_NAME
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$http = [Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(120)
try {
    $response = $http.GetAsync("https://$apiHostname/api/health").GetAwaiter().GetResult()
    try {
        $health = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ([int]$response.StatusCode -ne 200 -or $health.status -ne 'Ready') {
            throw "API health check failed (HTTP $([int]$response.StatusCode))."
        }
    }
    finally { $response.Dispose() }
    $response = $http.GetAsync("https://$apiHostname/api/accounts").GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 401) {
            throw "API anonymous-access check failed (HTTP $([int]$response.StatusCode))."
        }
    }
    finally { $response.Dispose() }
    $domain = (Get-OptionalDeploymentVariable PCS_WEB_CUSTOM_DOMAIN).ToLowerInvariant()
    $canonicalHost = if ($domain) { $domain } else { $webHostname }
    $response = $http.GetAsync("https://$canonicalHost/").GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 302 -or
            $response.Headers.Location.Host -ne 'login.microsoftonline.com') {
            throw "Web sign-in check failed (HTTP $([int]$response.StatusCode))."
        }
    }
    finally { $response.Dispose() }
    if ($domain) {
        # Every alias must reach the canonical URL through permanent redirects only.
        foreach ($alias in @("https://www.$domain/", "http://$domain/", "http://www.$domain/", "https://$webHostname/")) {
            $url = [Uri]$alias
            for ($hop = 1; $url.AbsoluteUri -ne "https://$domain/"; $hop++) {
                if ($hop -gt 2) { throw 'Web canonical redirect check failed (too many redirects).' }
                $response = $http.GetAsync($url).GetAwaiter().GetResult()
                try {
                    if ([int]$response.StatusCode -ne 301 -or -not $response.Headers.Location) {
                        throw "Web canonical redirect check failed (HTTP $([int]$response.StatusCode))."
                    }
                    $url = [Uri]::new($url, $response.Headers.Location)
                }
                finally { $response.Dispose() }
            }
        }
        Write-Host 'Canonical host checks passed: aliases permanently redirect to the custom domain.'
    }
    Write-Host 'Smoke checks passed: health 200, anonymous API 401, Web sign-in redirect 302.'
}
finally { $http.Dispose() }
