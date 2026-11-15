# Builds the release packages.
#   artifacts/  -> intermediate build output (ignored by git)
#   release/    -> FileManager.exe (portable) + FileManager-Setup.exe (installer)
#
# Usage: powershell -ExecutionPolicy Bypass -File build/build.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$root    = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish"
$release = Join-Path $root "release"
$common  = @("-c", "Release", "-r", "win-x64", "--self-contained", "true",
             "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
             "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none", "-p:Version=$Version")

if (-not (Test-Path (Join-Path $root "assets\icon.ico"))) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "make-icon.ps1")
}

Write-Host "==> Publishing app" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\FileManager\FileManager.csproj") @common -o (Join-Path $publish "app")
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

Write-Host "==> Publishing installer" -ForegroundColor Cyan
$payload = Join-Path $publish "app\FileManager.exe"
dotnet publish (Join-Path $root "src\FileManager.Setup\FileManager.Setup.csproj") @common "-p:PayloadPath=$payload" -o (Join-Path $publish "setup")
if ($LASTEXITCODE -ne 0) { throw "Setup publish failed" }

New-Item -ItemType Directory -Force $release | Out-Null
Copy-Item $payload (Join-Path $release "FileManager.exe") -Force
Copy-Item (Join-Path $publish "setup\FileManager-Setup.exe") (Join-Path $release "FileManager-Setup.exe") -Force

Write-Host "==> Release files" -ForegroundColor Green
Get-ChildItem $release | ForEach-Object { "{0,-26} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
