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
# SmoothFrame v3.0.0 :zap:

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v3.0.0:
- **All-New Violet UI & Theme Engine (Easy on the Eyes)**:
  - Deep violet neon aesthetic (`#0E0C17`, `#7C4DFF`) that eliminates eye strain during late-night gaming sessions ("теперь не режет глаза!").
  - Full Light and Dark theme switching (`App.xaml`, `App.xaml.cs`).
  - Redesigned oval action button with dynamic traveling neon laser beam animation (violet in standby, glowing crimson during active optimization and Minecraft boost).
  - High-precision CPU saver (`StopStoryboard`) when buttons are inactive.
- **Physics-Based Smooth Scrolling (`SmoothScrollViewer`)**:
  - Inertial Cubic-Ease wheel scrolling across all cards and settings views with 60 FPS fluidity.
- **Windows-Style Update Management & Notifications**:
  - Live GitHub Releases API checking over secure TLS 1.2/1.3.
  - **Skip Version**: Option to permanently silence update prompts for a specific release.
  - **Snooze / Remind Later**: Postpone notifications by 1 day, 3 days, 1 week, or 1 month.
  - **Pause Updates**: Pause update checks in Settings for up to 35 days (Windows Update style) with live resume badge.
- **Architectural Polish & Safety**:
  - Unified single-instance `UiSettings` model preventing settings overwrite races.
  - Windows `SessionEnding` / `WM_QUERYENDSESSION` support: SmoothFrame exits instantly on OS shutdown without blocking restart.
  - Normalized version parsing preventing false positive prompts.
  - 100% parity across all 6 localization dictionaries (`ru`, `en`, `de`, `es`, `fr`, `zh`) with 282 keys each.

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v3.0.0"
    target_commitish = "main"
    name = "SmoothFrame v3.0.0 - All-New Violet UI, Windows-Style Update Center & Smooth Scrolling"
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
