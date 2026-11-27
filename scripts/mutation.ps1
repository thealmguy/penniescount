<#
.SYNOPSIS
Runs the full Stryker mutation gate: all four configurations in parallel.

.DESCRIPTION
Each gate runs in its own temporary copy of the working tree. Stryker builds the
test project and swaps mutated assemblies into bin, so gates that share projects
must not share build outputs. The script fails unless every gate exits cleanly and
every report has only Killed, Ignored or CompileError mutants.
Reports are written to StrykerOutput\parallel-<timestamp>\<gate>.
#>
[CmdletBinding()]
param(
    [int]$LedgerConcurrency = 4,
    [int]$ApiConcurrency = 2,
    [int]$CoreConcurrency = 1,
    [int]$ContractsConcurrency = 1,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$reports = Join-Path $repo "StrykerOutput\parallel-$stamp"
$workspace = Join-Path ([IO.Path]::GetTempPath()) "PenniesCount.Mutation\$stamp-$PID"

$gates = @(
    @{ Name = 'ledger'; Project = 'tests\PenniesCount.Api.Tests'; Config = 'stryker-ledger.json'; Concurrency = $LedgerConcurrency }
    @{ Name = 'api'; Project = 'tests\PenniesCount.Api.Tests'; Config = 'stryker-config.json'; Concurrency = $ApiConcurrency }
    @{ Name = 'core'; Project = 'tests\PenniesCount.Core.Tests'; Config = 'stryker-working-days.json'; Concurrency = $CoreConcurrency }
    @{ Name = 'contracts'; Project = 'tests\PenniesCount.Web.Tests'; Config = 'stryker-details-validation.json'; Concurrency = $ContractsConcurrency }
)
$allowed = 'Killed', 'Ignored', 'CompileError'
$clock = [Diagnostics.Stopwatch]::StartNew()

try {
    foreach ($gate in $gates) {
        $copy = Join-Path $workspace $gate.Name
        robocopy $repo $copy /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 `
            /XD .git .vs .idea bin obj StrykerOutput TestResults /XF *.xlsx | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying the working tree for the $($gate.Name) gate failed." }
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
    if (-not $KeepWorkspace -and (Test-Path $workspace)) {
        Remove-Item -Recurse -Force $workspace -ErrorAction SilentlyContinue
    }
}
