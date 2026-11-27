Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Protect-DeploymentMessage {
    param([string]$Message)
    $safe = $Message -replace '(?i)\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b', '[identifier]'
    $safe = $safe -replace '(?i)[\w.-]+\.(azurewebsites\.net|database\.windows\.net)', '[hostname]'
    foreach ($value in @(Get-ChildItem Env:PCS_* | ForEach-Object Value | Where-Object { $_.Length -gt 3 })) {
        $safe = $safe.Replace($value, '[configuration]')
    }
    return $safe
}

function Invoke-AzureCli {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & az @Arguments --only-show-errors 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw (Protect-DeploymentMessage "Azure CLI failed (exit $LASTEXITCODE): $($output -join "`n")")
    }
    return $output -join "`n"
}

function Get-DeploymentVariable {
    param([Parameter(Mandatory)][string]$Name)
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match '^\$\(.+\)$') {
        throw "Required deployment variable $Name is missing."
    }
    return $value
}

function Register-DeploymentSecret {
    param([Parameter(Mandatory)][string]$Value)
    if ($env:TF_BUILD -eq 'True') {
        $escaped = $Value.Replace('%', '%AZP25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Host "##vso[task.setsecret]$escaped"
    }
}

function Assert-DeploymentAccount {
    $account = Invoke-AzureCli @('account', 'show', '-o', 'json') | ConvertFrom-Json
    if ($account.id -ne (Get-DeploymentVariable PCS_SUBSCRIPTION_ID) -or
        $account.tenantId -ne (Get-DeploymentVariable PCS_TENANT_ID)) {
        throw 'The service connection must target the configured subscription and tenant.'
    }
}
