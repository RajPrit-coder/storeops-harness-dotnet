#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Enforces the StoreOps line-coverage thresholds (spec §3.6) on a Cobertura report.

.DESCRIPTION
  Layers are derived from file paths, matching the module layout:
    Service layer   src/StoreOps.Api/Modules/<Module>/*Service.cs   >= 80%
    Route layer     src/StoreOps.Api/Modules/<Module>/*Routes.cs    >= 70%
    Shared          src/StoreOps.Api/Shared/**                      >= 60%
    Overall         every file in StoreOps.Api                      >= 70%
  A layer that matches no executable lines FAILS: an empty match means the gate is misconfigured
  (e.g. paths changed), and silently passing would hide every coverage regression.
  Exit code 0 = all thresholds met, 1 = at least one missed, 2 = no report found.
#>
param(
    [string] $ReportPath,
    [string] $ResultsDirectory = (Join-Path $PSScriptRoot '..' 'TestResults')
)

$ErrorActionPreference = 'Stop'

if (-not $ReportPath) {
    $latest = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $latest) {
        Write-Error "No coverage.cobertura.xml under $ResultsDirectory. Run eng/verify.ps1 (or dotnet test with coverage) first."
        exit 2
    }
    $ReportPath = $latest.FullName
}

[xml] $report = Get-Content -Raw -Path $ReportPath

# Cobertura lists async state machines and lambdas as separate classes of the same file, so
# lines are de-duplicated per (file, line) keeping the highest hit count.
$lines = @{}
foreach ($class in $report.coverage.packages.package.classes.class) {
    # Coverlet writes paths relative to a <source> root (e.g. "Modules\Activities\X.cs"), so normalise
    # separators and anchor with a leading '/' before matching.
    $file = '/' + (($class.filename -replace '\\', '/').TrimStart('/'))
    foreach ($line in $class.lines.line) {
        $key = "$file`:$($line.number)"
        $hits = [int] $line.hits
        if (-not $lines.ContainsKey($key) -or $lines[$key].Hits -lt $hits) {
            $lines[$key] = [pscustomobject] @{ File = $file; Hits = $hits }
        }
    }
}

$layers = @(
    [pscustomobject] @{ Name = 'Service layer'; Threshold = 80; Match = { param($f) $f -match '/Modules/[^/]+/[^/]*Service\.cs$' } }
    [pscustomobject] @{ Name = 'Route layer';   Threshold = 70; Match = { param($f) $f -match '/Modules/[^/]+/[^/]*Routes\.cs$' } }
    [pscustomobject] @{ Name = 'Shared';        Threshold = 60; Match = { param($f) $f -match '/Shared/' } }
    [pscustomobject] @{ Name = 'Overall';       Threshold = 70; Match = { param($f) $true } }
)

$failed = $false
$rows = foreach ($layer in $layers) {
    $inLayer = @($lines.Values | Where-Object { & $layer.Match $_.File })
    $covered = @($inLayer | Where-Object { $_.Hits -gt 0 }).Count
    $total = $inLayer.Count
    $percent = if ($total -eq 0) { 0.0 } else { [math]::Round(100.0 * $covered / $total, 1) }
    $ok = $total -gt 0 -and $percent -ge $layer.Threshold
    if (-not $ok) { $failed = $true }
    [pscustomobject] @{
        Layer     = $layer.Name
        Covered   = "$covered/$total"
        Coverage  = "$percent%"
        Threshold = "$($layer.Threshold)%"
        Result    = if ($ok) { 'PASS' } else { 'FAIL' }
    }
}

Write-Host "Coverage report: $ReportPath"
$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($failed) {
    Write-Host 'COVERAGE GATE: FAIL' -ForegroundColor Red
    exit 1
}

Write-Host 'COVERAGE GATE: PASS' -ForegroundColor Green
exit 0
