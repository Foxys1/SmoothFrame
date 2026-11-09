$ErrorActionPreference = "Stop"

Set-Location (Join-Path $PSScriptRoot "..")

$credInput = @"
protocol=https
host=github.com

"@

$credOutput = $credInput | git credential fill
$tokenMatch = $credOutput | Select-String '^password=(.+)$'
if (-not $tokenMatch) {
    throw "GitHub token not found in git credentials"
}
$token = $tokenMatch.Matches.Groups[1].Value

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/vnd.github+json"
    "User-Agent" = "SmoothFrame-Release-Bot"
    "X-GitHub-Api-Version" = "2022-11-28"
}

# Fix corrupted emoji artifacts (вљЎ -> :zap:) in previous releases
foreach ($prevTag in @("v2.1.0", "v2.1.1")) {
    try {
        $oldRel = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/$prevTag" -Method Get -Headers $headers
        if ($oldRel.body -match 'вљЎ' -or $oldRel.body -match '\u0432\u0459\u040E') {
            Write-Host "Fixing encoding artifact in $prevTag..."
            $fixedBody = $oldRel.body -replace 'вљЎ', ':zap:' -replace '\u0432\u0459\u040E', ':zap:'
            $patchJson = @{ body = $fixedBody } | ConvertTo-Json
            $patchBytes = [System.Text.Encoding]::UTF8.GetBytes($patchJson)
            Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/$($oldRel.id)" -Method Patch -Headers $headers -Body $patchBytes -ContentType "application/json; charset=utf-8" | Out-Null
            Write-Host "Successfully fixed release notes for $prevTag"
        }
    } catch {
        Write-Warning "Could not update $($prevTag): $_"
    }
}

$releaseNotes = @'
# SmoothFrame v2.2.0 :zap:

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v2.2.0:
- **Process Power Throttling & Quality of Service (QoS)**:
  - Native Win32 `SetProcessInformation(ProcessPowerThrottling)` prevents Windows 11 CPU downclocking for active games (`HighQoS`).
  - Automatically shifts heavy background apps (Discord, Chrome, Telegram, Spotify, etc.) into `EcoQoS` (Efficiency Mode) to eliminate thermal and thread contention.
  - 100% in-memory with zero permanent registry modifications.
- **Windowed Game Optimization (DXGI Flip Model Promotion)**:
  - Automatically activates the Windows 11 Auto-Flip presentation model upgrade (`SwapEffectUpgradeCache = 1`) for borderless windowed games.
  - Eliminates 15-30ms of DWM composition lag and unlocks G-Sync/FreeSync and Auto HDR in borderless mode without tearing.
- **1:1 Linear Hardware Mouse Input**:
  - Enforces pure 1:1 hardware sensor tracking by eliminating Windows "Enhance Pointer Precision" (EPP) curve during gameplay.
  - Captures user's exact baseline mouse parameters and restores them on exit.
- **System Diagnostics & Gaming Health Inspector**:
  - Real-time read-only inspection for VBS (Virtualization-Based Security / Core Isolation) with one-click shortcut to Windows settings.
  - Real-time hardware status for HAGS (Hardware-Accelerated GPU Scheduling) and Windows Game Mode.
- **UI & Bento Grid Integration**:
  - New modular quick-control toggles and dedicated Gaming Health card with crisp ClearType subpixel rendering.
  - 100% translation key parity across all 6 languages (EN, RU, DE, ES, FR, ZH).
- **Interactive Close Confirmation Modal**:
  - Choose between "Minimize to System Tray" (keep optimizations active) or "Exit Completely" (safe WAL rollback).

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v2.2.0"
    target_commitish = "main"
    name = "SmoothFrame v2.2.0 - Process QoS, Windowed Flip Model & Gaming Health"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for v2.2.0..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/v2.2.0" -Method Get -Headers $headers
    Write-Host "Found existing release: $($release.html_url)"
}

$assetPath = "installer/SmoothFrame_Setup.exe"
if (Test-Path $assetPath) {
    $assetName = [System.IO.Path]::GetFileName($assetPath)
    $sizeMB = [math]::Round((Get-Item $assetPath).Length / 1MB, 2)
    Write-Host "Uploading asset $assetName ($sizeMB MB)..."

    # Check if asset already exists in release
    $existingAsset = $release.assets | Where-Object { $_.name -eq $assetName }
    if ($existingAsset) {
        Write-Host "Deleting existing asset $($existingAsset.id)..."
        Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/assets/$($existingAsset.id)" -Method Delete -Headers $headers
    }

    $uploadUrl = $release.upload_url -replace '\{\?name,label\}', "?name=$assetName"

    $assetHeaders = @{
        "Authorization" = "Bearer $token"
        "Accept" = "application/vnd.github+json"
        "User-Agent" = "SmoothFrame-Release-Bot"
        "X-GitHub-Api-Version" = "2022-11-28"
        "Content-Type" = "application/octet-stream"
    }

    $uploadResult = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $assetHeaders -InFile $assetPath -TimeoutSec 600
    Write-Host "[SUCCESS] Asset uploaded successfully: $($uploadResult.browser_download_url)"
} else {
    Write-Warning "Asset $assetPath not found!"
}
