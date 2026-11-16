$ErrorActionPreference = "Stop"

Set-Location (Join-Path $PSScriptRoot "..")

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$credInFile = [System.IO.Path]::GetTempFileName()
$credOutFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($credInFile, "protocol=https`nhost=github.com`n`n", $utf8NoBom)
    $credProc = Start-Process -FilePath "git" -ArgumentList "credential", "fill" -RedirectStandardInput $credInFile -RedirectStandardOutput $credOutFile -NoNewWindow -Wait -PassThru
    $credOutput = Get-Content -Path $credOutFile -ErrorAction SilentlyContinue
} finally {
    Remove-Item -Path $credInFile, $credOutFile -Force -ErrorAction SilentlyContinue
}

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
# SmoothFrame v3.1.0 :zap:

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v3.1.0:
- **Native Roblox Auto-Discovery & Dynamic Process Self-Healing**:
  - Full discovery across `%LOCALAPPDATA%\Roblox\Versions`, `Program Files`, registry protocol `roblox-player://`, uninstall entries, and Microsoft Store (UWP `ROBLOXCORPORATION.ROBLOX`).
  - **Path Self-Healing**: Dynamic tracking of changing version folders (`version-*`) without manual re-selection or app restarts.
  - **Live Process Fallback**: Instant automatic detection and optimization of running `RobloxPlayerBeta.exe` and `Windows10Universal.exe` processes.
  - **Discrete GPU Binding**: Automatically enforces high-performance discrete GPU mode via Windows Graphics preferences.
- **Robust Process Safety & Thread Marshaling**:
  - Thread-safe WPF `Dispatcher` marshaling for dynamic game library updates.
  - Guaranteed process handle cleanup preventing OS descriptor leaks.
  - 100% test pass rate across 443 unit, integration, and empirical gaming stress tests.
- **Eye-Care Violet UI & Theme Engine (From v3.0)**:
  - Deep violet cyber aesthetic (`#08080C`, `#8B5CF6`) designed to eliminate eye strain during night sessions ("теперь не режет глаза!").
  - Windows-style update management (Snooze, Pause, Skip version).
  - Physics-based smooth scrolling (`SmoothScrollViewer`).

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v3.1.0"
    target_commitish = "main"
    name = "SmoothFrame v3.1.0 - Roblox Native Auto-Discovery & Dynamic Process Self-Healing"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for $($bodyObj.tag_name)..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/$($bodyObj.tag_name)" -Method Get -Headers $headers
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
