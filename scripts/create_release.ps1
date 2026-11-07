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

$releaseNotes = @'
# SmoothFrame v2.1.1 ⚡

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v2.1.1:
- **Universal Game I/O Priority Streaming**:
  - Native `ntdll!NtSetInformationProcess` (`IoPriorityHigh`) prioritizes real-time texture and asset streaming for active games.
  - Automatically deprioritizes background disk activity (`IoPriorityVeryLow`) to eliminate asset-load hitching.
  - 100% reversible in-memory tuning with zero permanent registry modifications.
- **Zero-Ping Network Low-Latency Stack**:
  - Disables Windows Multimedia Network Throttling during gameplay.
  - Activates `TcpAckFrequency = 1` and `TCPNoDelay = 1` on active gaming network interfaces for instantaneous packet response.
  - Safely restores original registry network state via WAL snapshot on exit.
- **AudioDG Core Isolation & DPC Latency Shield**:
  - Automatically shields Windows Audio Device Graph (`audiodg.exe`) from DPC spikes and thread starvation.
  - Pins audio processing to a dedicated CPU core and elevates scheduling priority to eliminate micro-crackles and audio dropouts.
- **Multi-Vendor Shader Cache Cleaner**:
  - One-click safe cleanup for DirectX, NVIDIA, AMD, and Intel shader caches with locked-file skip protection.
- **Multi-Launcher Auto-Detection**:
  - Automatic game detection expanded to Steam, Epic Games, GOG Galaxy, Ubisoft Connect, EA Desktop / Origin, and Riot Games.
- **Code Quality & Memory Hardening**:
  - Resolved process handle lifetimes in audio management services.
  - Dynamic multi-language localization for shader cleanup metrics across all 6 languages.

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v2.1.1"
    target_commitish = "main"
    name = "SmoothFrame v2.1.1 - Universal Game Engine, Zero-Ping & I/O Priority"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for v2.1.1..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/v2.1.1" -Method Get -Headers $headers
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
