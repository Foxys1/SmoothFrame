<#
.SYNOPSIS
    Builds SmoothFrame and creates the standalone single-file installer.

.DESCRIPTION
    1. Builds the solution.
    2. Runs test suites (Runner & Challenge).
    3. Publishes SmoothFrame.UI as a self-contained single-file bundle.
    4. Archives the UI payload into publish_payload.zip.
    5. Publishes the standalone installer (installer/SmoothFrame_Setup.exe).
#>

param(
    [switch]$SkipTests = $false
)

$ErrorActionPreference = 'Stop'

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "       SmoothFrame Build Pipeline        " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Build Solution
Write-Host "`n[1/5] Building SmoothFrame.sln..." -ForegroundColor Yellow
dotnet build SmoothFrame.sln -c Release -m:1
if ($LASTEXITCODE -ne 0) { throw "Solution build failed." }

# 2. Run Tests
if (-not $SkipTests) {
    Write-Host "`n[2/5] Running Safety & Core Tests..." -ForegroundColor Yellow
    dotnet run --project tests/SmoothFrame.Runner -c Release
    if ($LASTEXITCODE -ne 0) { throw "SmoothFrame.Runner tests failed." }

    Write-Host "`n[2/5] Running Empirical Challenge Tests..." -ForegroundColor Yellow
    dotnet run --project tests/SmoothFrame.Challenge -c Release
    if ($LASTEXITCODE -ne 0) { throw "SmoothFrame.Challenge tests failed." }
} else {
    Write-Host "`n[2/5] Skipping tests (-SkipTests specified)..." -ForegroundColor DarkGray
}

# 3. Publish UI Single-File
Write-Host "`n[3/5] Publishing SmoothFrame.UI..." -ForegroundColor Yellow
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }
dotnet publish src/SmoothFrame.UI/SmoothFrame.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish -m:1
if ($LASTEXITCODE -ne 0) { throw "SmoothFrame.UI publish failed." }

# 4. Pack Payload Archive
Write-Host "`n[4/5] Creating installer payload..." -ForegroundColor Yellow
$payloadDir = "src/SmoothFrame.Installer/Payload"
if (-not (Test-Path $payloadDir)) { New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null }
$zipPath = Join-Path $payloadDir "publish_payload.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path "publish").Path, (Resolve-Path $payloadDir).Path + "\publish_payload.zip", [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item "publish" -Recurse -Force

# 5. Publish Installer
Write-Host "`n[5/5] Publishing SmoothFrame_Setup.exe..." -ForegroundColor Yellow
if (-not (Test-Path "installer")) { New-Item -ItemType Directory -Path "installer" -Force | Out-Null }
dotnet publish src/SmoothFrame.Installer/SmoothFrame.Installer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o installer -m:1
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed." }

$installerPath = "installer/SmoothFrame_Setup.exe"
if (Test-Path $installerPath) {
    $sizeMB = [math]::Round((Get-Item $installerPath).Length / 1MB, 2)
    Write-Host "`n[SUCCESS] Installer built successfully: $installerPath ($sizeMB MB)" -ForegroundColor Green
} else {
    throw "Installer binary was not found after publishing."
}
