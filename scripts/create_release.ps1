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
        Write-Warning "Could not update $prevTag: $_"
    }
}

$releaseNotes = @'
# SmoothFrame v2.1.2 :zap:

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v2.1.2:
- **Interactive Close Confirmation Modal**:
  - Clicking the close button (`X`) or `Alt+F4` now presents an elegant Obsidian modal dialog rather than silently vanishing.
  - Choose between **"Minimize to System Tray"** (maintaining active low-latency tuning & auto-boost) or **"Exit Completely"** (safely restoring all OS settings and terminating).
  - Includes a "Remember my choice" option and a dedicated toggle in Settings (`PromptOnClose`).
- **Subpixel Text Rendering & ClearType Fidelity**:
  - Completely resolved hover text blur by decoupling drop shadows onto dedicated non-text layers and eliminating fractional scale transforms.
- **Universal Game Optimization Suite**:
  - Native NT I/O Priority streaming (`IoPriorityHigh` for game assets, `IoPriorityVeryLow` for background apps).
  - Zero-Ping network stack (`TCPNoDelay = 1`, `TcpAckFrequency = 1`, and disabled multimedia throttling).
  - AudioDG CPU core isolation and DPC latency shield for glitch-free audio.
  - Safe multi-vendor DirectX, NVIDIA, AMD, and Intel shader cache cleaner.
  - Auto-discovery for Steam, Epic, GOG, Ubisoft, EA, and Riot games.

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v2.1.2"
    target_commitish = "main"
    name = "SmoothFrame v2.1.2 - Close Action Dialog & ClearType Fidelity"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for v2.1.2..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/v2.1.2" -Method Get -Headers $headers
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
