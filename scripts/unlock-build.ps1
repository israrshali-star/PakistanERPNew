# Stop build servers and delete locked obj/bin output so the next build can write DLLs.
$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Stopping running app and build servers..."
Get-Process "PakistanAccountingERP.Web", iisexpress -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build-server shutdown | Out-Null
Get-Process MSBuild, VBCSCompiler -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "Removing obj and bin folders under src (includes *dswa.cache.json)..."
Get-ChildItem -Path (Join-Path $root "src") -Recurse -Directory |
    Where-Object { $_.Name -in @("obj", "bin") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

Write-Host "Done. Close Visual Studio if it is open, then rebuild once (CLI or VS, not both)."
