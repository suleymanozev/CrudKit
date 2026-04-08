#!/usr/bin/env pwsh
# CrudKit build, test, and pack script

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [switch]$SkipTests,
    [switch]$Pack,
    [switch]$Push,
    [string]$NuGetSource = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey
)

$ErrorActionPreference = "Stop"
$OutputDir = "$PSScriptRoot/nupkg"

Write-Host "=== CrudKit Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Version: $Version"

# Clean
Write-Host "`n--- Clean ---" -ForegroundColor Yellow
dotnet clean CrudKit.slnx -c $Configuration --verbosity quiet
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

# Build
Write-Host "`n--- Build ---" -ForegroundColor Yellow
dotnet build CrudKit.slnx -c $Configuration -p:Version=$Version
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# Test
if (-not $SkipTests) {
    Write-Host "`n--- Test ---" -ForegroundColor Yellow
    dotnet test CrudKit.slnx -c $Configuration --no-build --verbosity minimal
    if ($LASTEXITCODE -ne 0) { Write-Error "Tests failed"; exit 1 }
    Write-Host "All tests passed!" -ForegroundColor Green
}

# Pack
if ($Pack -or $Push) {
    Write-Host "`n--- Pack ---" -ForegroundColor Yellow
    dotnet pack CrudKit.slnx -c $Configuration -p:Version=$Version -o $OutputDir --no-build
    if ($LASTEXITCODE -ne 0) { Write-Error "Pack failed"; exit 1 }

    Write-Host "`nPackages created:" -ForegroundColor Green
    Get-ChildItem $OutputDir/*.nupkg | ForEach-Object {
        Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1KB)) KB)"
    }
}

# Push
if ($Push) {
    if (-not $ApiKey) { Write-Error "ApiKey is required for push. Use -ApiKey YOUR_KEY"; exit 1 }

    Write-Host "`n--- Push to $NuGetSource ---" -ForegroundColor Yellow
    Get-ChildItem $OutputDir/*.nupkg | ForEach-Object {
        Write-Host "  Pushing $($_.Name)..."
        dotnet nuget push $_.FullName --api-key $ApiKey --source $NuGetSource --skip-duplicate
        if ($LASTEXITCODE -ne 0) { Write-Error "Push failed for $($_.Name)"; exit 1 }
    }
    Write-Host "All packages pushed!" -ForegroundColor Green
}

Write-Host "`n=== Done ===" -ForegroundColor Cyan
