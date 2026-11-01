param([int]$Count = 3)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location (Join-Path $root 'core')
try {
    Write-Output "Running pool generator (target: $Count accounts)..."
    go run ./cmd/pool-gen -count $Count -pool (Join-Path $root 'dist\provision\pool.json')
    Copy-Item (Join-Path $root 'dist\provision\pool.json') (Join-Path $root 'mobile\android\app\src\main\assets\accounts_pool.json') -Force
    Write-Output "Updated pool.json and mobile assets."
} finally {
    Pop-Location
}
