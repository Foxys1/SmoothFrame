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
# SmoothFrame v2.1.0 ⚡

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v2.1.0:
- **Laser Precision Monolith Installer**: Completely redesigned setup wizard featuring interactive micro-scale button physics, high-contrast laser borders, and physical verification chips (`NT_TIMER: 0.50 ms`, `UDP_HUD: 127.0.0.1`, `SAFETY: 100% REVERSIBLE`).
- **Orchestrated First-Launch Onboarding**: Staggered entrance animations, real-time language capsule preview, and a calibrated 1-second pause before smooth multi-language greeting cycling.
- **Enhanced Bento Grid & Quick Control Deck**: Modular quick-control cards with one-click toggles for Auto-Boost on game launch, P-Core isolation, background app throttling (Discord/browser silencing), and Windows Standby RAM purge.
- **Robust Internationalization (i18n)**:
  - 100% key coverage across all 6 supported languages: English, Russian, German, Spanish, French, and Simplified Chinese.
  - Automatic English fallback protection layer in `LocalizationManager` preventing any missing strings.
- **Dedicated Minecraft Mode (NeoForge 1.21.1)**:
  - High-frequency UDP Loopback HUD (127.0.0.1:28511) streaming live FPS, 1% Low FPS, frametime, and heap telemetry.
  - Frustum & distance entity culling, zero-stall chunk streamer, and dynamic background frame limiter.
  - One-click JVM GC profiles (Generational ZGC & Aikar G1GC) with synchronous memory allocation.
- **System-Level Gaming Optimizations**:
  - 0.50 ms high-precision multimedia timer via native `NtSetTimerResolution`.
  - Automated Ultimate Performance power plan management with deduplication prevention.
  - Hardware GPU binding and RAM standby cache purge.
  - Zero OS handle leaks, memory safety hardening, and 100% reversible WAL state recovery.

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v2.1.0"
    target_commitish = "main"
    name = "SmoothFrame v2.1.0 - Laser Monolith UI & Enhanced Control Deck"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for v2.1.0..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/v2.1.0" -Method Get -Headers $headers
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
