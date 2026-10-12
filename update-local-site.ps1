$ErrorActionPreference = "Continue"
$root = "C:\Users\israr\Downloads\PakistanAccountingERP"
$log = "C:\Publish\erp-result.txt"
$live = "C:\Publish\PakistanAccountingERP"
$staging = "C:\Publish\PakistanAccountingERP-staging"

function Log([string]$Message) {
    $line = $Message
    Add-Content -Path $log -Value $line
    Write-Host $line
}

New-Item -ItemType Directory -Force -Path "C:\Publish" | Out-Null
Set-Content -Path $log -Value ("ERP publish " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
Set-Location $root
Log "---- branch ----"
Log (git branch -a 2>&1 | Out-String)
Log "---- remote ----"
Log (git remote -v 2>&1 | Out-String)
Log "---- status ----"
Log (git status -sb 2>&1 | Out-String)

$layoutPath = Join-Path $root "src\Application\Common\ChequeLeafLayout.cs"
if (Test-Path -LiteralPath $layoutPath) {
    $layout = Get-Content -LiteralPath $layoutPath -Raw
    if ($layout -notmatch "LandscapePageOriginX") {
        if ($layout -match "A4LongMm") {
            $addition = "`r`n    public static float LandscapePageOriginX => (A4LongMm - WidthMm) / 2f;`r`n    public static float PortraitPageOriginX => (A4ShortMm - HeightMm) / 2f;`r`n"
            $end = $layout.LastIndexOf("}")
            $layout = $layout.Insert($end, $addition)
            Set-Content -LiteralPath $layoutPath -Value $layout -Encoding UTF8
            Log "Added the missing cheque page lines."
        }
        else {
            Log "Cheque layout file has no A4 sizes."
        }
    }
    else {
        Log "Cheque layout file already has the page lines."
    }
}
else {
    Log "Cheque layout file was not found."
}

$csproj = Join-Path $root "src\Web\PakistanAccountingERP.Web.csproj"
if (-not (Test-Path -LiteralPath $csproj)) {
    Log "Project file not found."
    Log "FAILED"
    exit 1
}

New-Item -ItemType Directory -Force -Path $live, $staging | Out-Null
$html = "<html><body><h1>Updating the site.</h1></body></html>".PadRight(512)
Set-Content -LiteralPath (Join-Path $live "app_offline.htm") -Value $html -Encoding Ascii
Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*PakistanAccountingERP.Web.dll*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 8

$dotnet = "dotnet"
$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (Test-Path -LiteralPath $dotnetExe) {
    $dotnet = $dotnetExe
}

Log "Building..."
$buildLog = Join-Path $env:TEMP "erp-build.txt"
& $dotnet publish $csproj -c Release --self-contained false -o $staging > $buildLog 2>&1
$buildCode = $LASTEXITCODE
Get-Content -LiteralPath $buildLog | ForEach-Object { Log $_ }
if ($buildCode -ne 0) {
    Remove-Item -LiteralPath (Join-Path $live "app_offline.htm") -Force -ErrorAction SilentlyContinue
    Log "FAILED. The build error is above."
    exit 1
}

$failed = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $staging -Recurse -File -Force | ForEach-Object {
    if ($_.Extension -eq ".exe" -or $_.Name -eq "app_offline.htm") {
        return
    }
    $relative = $_.FullName.Substring($staging.Length).TrimStart("\")
    $destination = Join-Path $live $relative
    $folder = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $folder)) {
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
    }
    try {
        [System.IO.File]::Copy($_.FullName, $destination, $true)
    }
    catch {
        try {
            $backup = $destination + "." + (Get-Date -Format "HHmmss") + ".old"
            [System.IO.File]::Move($destination, $backup)
            [System.IO.File]::Copy($_.FullName, $destination, $true)
        }
        catch {
            $failed.Add($relative) | Out-Null
            Log ("Could not replace " + $relative)
        }
    }
}

Remove-Item -LiteralPath (Join-Path $live "app_offline.htm") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $live "PakistanAccountingERP.Web.exe") -Force -ErrorAction SilentlyContinue

$dll = Join-Path $live "PakistanAccountingERP.Web.dll"
if (Test-Path -LiteralPath $dll) {
    $bytes = [System.IO.File]::ReadAllBytes($dll)
    $text = [System.Text.Encoding]::Unicode.GetString($bytes)
    $hasNewPages = $text.Contains("cheque-nudges-2026-10-06")
    Log ("Program file time: " + (Get-Item -LiteralPath $dll).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
    Log ("New cheque pages are in the program: " + $hasNewPages)
}
else {
    Log "Program file was not copied."
}

Log ("Files still open: " + $failed.Count)
if ($failed.Count -eq 0) {
    Log "SUCCESS"
}
else {
    Log "PARTIAL"
}
Log "Open the site and press Ctrl+F5."
Log "Result file: C:\Publish\erp-result.txt"
