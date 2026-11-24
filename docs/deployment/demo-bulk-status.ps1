<#
.SYNOPSIS
  Demonstrates PATCH /api/activities/bulk-status against the StoreOps container (for the DEPLOYMENT.md screenshot).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File docs\deployment\demo-bulk-status.ps1

  Works in Windows PowerShell 5.1 and PowerShell 7. The container must be running: podman compose up -d
#>
param([string] $BaseUrl = 'http://localhost:5000')

$ErrorActionPreference = 'Stop'
$manager = @{ Authorization = 'Bearer dev-token-store-manager' }       # usr-sm-001, store-001
$store2 = @{ Authorization = 'Bearer dev-token-store-002-manager' }    # usr-sm-002, store-002

function Show-Step([string] $title) {
    Write-Host ''
    Write-Host "== $title ==" -ForegroundColor Cyan
}

function New-Activity($headers, [string] $title, [string] $category) {
    $body = @{ title = $title; category = $category } | ConvertTo-Json
    Invoke-RestMethod "$BaseUrl/api/activities" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
}

Show-Step 'Running container'
podman ps --format '{{.Names}}  {{.Image}}  {{.Status}}  {{.Ports}}'

Show-Step '1. Request without a token (Production requires auth)'
try {
    Invoke-RestMethod "$BaseUrl/api/activities" | Out-Null
}
catch {
    $response = $_.Exception.Response
    $message = $_.ErrorDetails.Message
    if (-not $message -and $response -and $response.GetType().GetMethod('GetResponseStream')) {
        # Windows PowerShell 5.1 leaves ErrorDetails empty; read the body from the response stream.
        $message = (New-Object System.IO.StreamReader($response.GetResponseStream())).ReadToEnd()
    }
    Write-Host "HTTP $([int]$response.StatusCode)  $message" -ForegroundColor Yellow
}

Show-Step '2. Create two activities in store-001 and one in store-002'
$a1 = New-Activity $manager 'Restock dairy aisle' 'RESTOCKING'
$a2 = New-Activity $manager 'Planogram reset aisle 4' 'PLANOGRAM'
$a3 = New-Activity $store2 'Store 002 audit' 'AUDIT'
@($a1, $a2, $a3) | Format-Table id, storeId, title, status -AutoSize

Show-Step '3. Shift handover: PATCH /api/activities/bulk-status -> DONE (2 own, 1 other store, 1 unknown id)'
$body = @{
    status      = 'DONE'
    activityIds = @($a1.id, $a2.id, $a3.id, 'act-does-not-exist')
    note        = 'Evening shift handover'
} | ConvertTo-Json
$result = Invoke-RestMethod "$BaseUrl/api/activities/bulk-status" -Method Patch -Headers $manager -ContentType 'application/json' -Body $body
Write-Host "HTTP 200  requested=$($result.requested)  updated=$($result.updated)  unchanged=$($result.unchanged)  failed=$($result.failed)" -ForegroundColor Green
$result.results | Format-Table activityId, outcome, previousStatus, newStatus, @{ Name = 'error'; Expression = { $_.error.code } } -AutoSize

Show-Step "4. Audit entry for $($a1.id)"
Invoke-RestMethod "$BaseUrl/api/activities/$($a1.id)/audit" -Headers $manager |
    Format-Table previousStatus, newStatus, changedBy, source, note -AutoSize

Show-Step '5. store-002 activity was not touched'
$after = Invoke-RestMethod "$BaseUrl/api/activities/$($a3.id)" -Headers $store2
Write-Host "$($after.id)  status=$($after.status)" -ForegroundColor Green
