#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Runs every automated StoreOps check, in the order a harness hard gate or CI would.

.DESCRIPTION
  1. dotnet build              analyzers, StyleCop, nullable, banned APIs; warnings are errors
  2. dotnet format --verify    formatting and code-style drift
  3. dotnet test + coverage    endpoint, service, architecture tests (coverlet, Cobertura)
  4. eng/check-coverage.ps1    spec §3.6 thresholds per layer
  Stops at the first failing step and exits non-zero.
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $root
try {
    $results = Join-Path $root 'TestResults' (Get-Date -Format 'yyyyMMdd-HHmmss')

    $steps = @(
        @{ Name = 'build';    Run = { dotnet build StoreOps.slnx --nologo } }
        @{ Name = 'format';   Run = { dotnet format StoreOps.slnx --verify-no-changes } }
        @{ Name = 'test';     Run = { dotnet test StoreOps.slnx --no-build --nologo --settings coverage.runsettings --collect 'XPlat Code Coverage' --results-directory $results } }
        @{ Name = 'coverage'; Run = { & (Join-Path $PSScriptRoot 'check-coverage.ps1') -ResultsDirectory $results } }
    )

    foreach ($step in $steps) {
        Write-Host "==> $($step.Name)" -ForegroundColor Cyan
        & $step.Run
        if ($LASTEXITCODE -ne 0) {
            Write-Host "VERIFY: FAIL at '$($step.Name)' (exit $LASTEXITCODE)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }

    Write-Host 'VERIFY: PASS' -ForegroundColor Green
}
finally {
    Pop-Location
}
