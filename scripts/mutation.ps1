<#
.SYNOPSIS
Runs the full Stryker mutation gate: all five configurations in parallel.

.DESCRIPTION
Each gate runs in its own copy of the working tree. Stryker builds the test project
and swaps mutated assemblies into bin, so gates that share projects must not share
build outputs. The copies persist between runs: each run mirrors the working tree
into them but keeps each gate's bin and obj, so Stryker skips its NuGet restore retry
and builds incrementally. A concurrent run that cannot lock the persistent copies
uses fresh temporary ones instead; -Clean discards the persistent copies first.
The script fails unless every gate exits cleanly and every report has only Killed,
Ignored or CompileError mutants.
Reports are written to StrykerOutput\parallel-<timestamp>\<gate>.
#>
[CmdletBinding()]
param(
    [Alias('Gate')]
    [ValidateSet('ledger', 'api', 'core', 'contracts', 'web')]
    [string]$GateName,
    [int]$LedgerConcurrency = 5,
    [int]$ApiConcurrency = 2,
    [int]$CoreConcurrency = 1,
    [int]$ContractsConcurrency = 1,
    [int]$WebConcurrency = 1,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$reports = Join-Path $repo "StrykerOutput\parallel-$stamp"
$repoKey = -join [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($repo.ToLowerInvariant())))[0..11]
$root = Join-Path ([IO.Path]::GetTempPath()) 'PenniesCount.Mutation'
New-Item -ItemType Directory -Force -Path $root | Out-Null
$workspace = Join-Path $root $repoKey
$temporary = $false
$lease = $null
try {
    $lease = [IO.File]::Open("$workspace.lock", 'OpenOrCreate', 'ReadWrite', 'None')
}
catch [IO.IOException] {
    Write-Host 'Another mutation run holds the persistent workspace; using a temporary copy.'
    $workspace = Join-Path $root "$repoKey-$stamp-$PID"
    $temporary = $true
}
if ($Clean -and -not $temporary -and (Test-Path $workspace)) { Remove-Item -Recurse -Force $workspace }

$gates = @(
    @{ Name = 'ledger'; Project = 'tests\PenniesCount.Api.Tests'; Config = 'stryker-ledger.json'; Concurrency = $LedgerConcurrency }
    @{ Name = 'api'; Project = 'tests\PenniesCount.Api.Tests'; Config = 'stryker-config.json'; Concurrency = $ApiConcurrency }
    @{ Name = 'core'; Project = 'tests\PenniesCount.Core.Tests'; Config = 'stryker-working-days.json'; Concurrency = $CoreConcurrency }
    @{ Name = 'contracts'; Project = 'tests\PenniesCount.Web.Tests'; Config = 'stryker-details-validation.json'; Concurrency = $ContractsConcurrency }
    @{ Name = 'web'; Project = 'tests\PenniesCount.Web.Tests'; Config = 'stryker-canonical-host.json'; Concurrency = $WebConcurrency }
)
if ($GateName) { $gates = @($gates | Where-Object Name -eq $GateName) }
$allowed = 'Killed', 'Ignored', 'CompileError'
$clock = [Diagnostics.Stopwatch]::StartNew()

try {
    foreach ($gate in $gates) {
        $copy = Join-Path $workspace $gate.Name
        robocopy $repo $copy /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 `
            /XD .git .vs .idea bin obj StrykerOutput TestResults /XF *.xlsx | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying the working tree for the $($gate.Name) gate failed." }
        # Stryker resolves references before its build; dependency changes must reach obj first.
        & dotnet restore (Join-Path $copy $gate.Project) --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw "Restoring dependencies for the $($gate.Name) gate failed." }
        $gate.Output = Join-Path $reports $gate.Name
        $gate.Log = Join-Path $reports "$($gate.Name).log"
        New-Item -ItemType Directory -Force -Path $gate.Output | Out-Null
        $gate.Process = Start-Process dotnet -NoNewWindow -PassThru `
            -WorkingDirectory (Join-Path $copy $gate.Project) `
            -RedirectStandardOutput $gate.Log -RedirectStandardError "$($gate.Log).err" `
            -ArgumentList 'stryker', '--config-file', $gate.Config, '--concurrency', $gate.Concurrency,
                '--output', "`"$($gate.Output)`""
        $null = $gate.Process.Handle
    }

    $failures = @()
    foreach ($gate in $gates) {
        $gate.Process.WaitForExit()
        $gate.Seconds = [int]($gate.Process.ExitTime - $gate.Process.StartTime).TotalSeconds
        $report = Get-ChildItem $gate.Output -Recurse -Filter mutation-report.json | Select-Object -First 1
        if ($gate.Process.ExitCode -ne 0 -or -not $report) {
            $failures += "$($gate.Name): Stryker exited with $($gate.Process.ExitCode); see $($gate.Log)"
            continue
        }
        $mutants = (Get-Content $report.FullName -Raw | ConvertFrom-Json).files.PSObject.Properties |
            ForEach-Object { $_.Value.mutants }
        $gate.Killed = @($mutants | Where-Object status -eq 'Killed').Count
        foreach ($mutant in @($mutants | Where-Object status -notin $allowed)) {
            $failures += "$($gate.Name): $($mutant.status) $($mutant.mutatorName) at line $($mutant.location.start.line)"
        }
    }

    $gates | ForEach-Object {
        [pscustomobject]@{ Gate = $_.Name; Seconds = $_.Seconds; Killed = $_.Killed; Concurrency = $_.Concurrency }
    } | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Total wall-clock: $([int]$clock.Elapsed.TotalSeconds) s. Reports: $reports"
    if ($failures) {
        $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
    exit 0
}
finally {
    foreach ($gate in $gates) {
        if ($gate.Process -and -not $gate.Process.HasExited) { Stop-Process -Id $gate.Process.Id }
    }
    if ($temporary -and (Test-Path $workspace)) {
        Remove-Item -Recurse -Force $workspace -ErrorAction SilentlyContinue
    }
    if ($lease) { $lease.Dispose() }
}
