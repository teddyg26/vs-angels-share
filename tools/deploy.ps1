param(
    [string]$ModDir = "$env:APPDATA\VintagestoryData\Mods\AngelsShare_dev"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Repo root: $RepoRoot"
Write-Host "Mod dir:   $ModDir"

if (!(Test-Path $ModDir)) {
    Write-Host "Creating mod directory..."
    New-Item -ItemType Directory -Path $ModDir | Out-Null
}

Write-Host "Deploying assets..."
robocopy "$RepoRoot\assets" "$ModDir\assets" /MIR | Out-Null

Write-Host "Deploying mod metadata..."
Copy-Item "$RepoRoot\modinfo.json" "$ModDir\modinfo.json" -Force

if (Test-Path "$RepoRoot\modicon.png") {
    Copy-Item "$RepoRoot\modicon.png" "$ModDir\modicon.png" -Force
}

Write-Host "Building C# project..."
dotnet build "$RepoRoot\AngelsShare.csproj" -c Debug

Write-Host "Deploy complete."