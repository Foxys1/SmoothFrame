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
# SmoothFrame v2.3.0 :zap:

A modern, transparent, safe, and 100% reversible game optimization suite for Windows 10 & 11.

## What's New in v2.3.0:
- **Process Memory Priority Optimization (RAM Paging Protection)**:
  - Native Win32 `SetProcessInformation(ProcessMemoryPriority)` prioritizes active game RAM pages (`MEMORY_PRIORITY_NORMAL = 5`) and shifts noisy background apps to `MEMORY_PRIORITY_VERY_LOW (1)`.
  - Zero registry edits and 100% in-memory WAL-backed rollback on game exit or crash recovery.
- **Smart Alt+Tab Focus Tracking**:
  - Lightweight out-of-context Win32 `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND`) detects when you Alt+Tab out of the game.
  - Automatically restores background apps while you browse and re-applies full prioritization the moment you return to the game window.
- **Extended System Diagnostics & Gaming Health Bento Inspector**:
  - **Display Refresh Rate Check**: Uses `EnumDisplaySettingsEx` to detect when a high-refresh monitor drops to 60 Hz, with a one-click button to open Windows Display Settings (`ms-settings:display`).
  - **DXGI Video Memory (VRAM) Budget Check**: Queries `IDXGIAdapter3::QueryVideoMemoryInfo` in real time to flag VRAM overcommit before frame-time spikes happen.
  - **CPU Frequency Throttling Check**: Reads `CallNtPowerInformation(ProcessorInformation)` across cores to detect active firmware or thermal throttling.
- **UI & Stability**:
  - Expanded Overview Gaming Health section into a balanced 3x2 Bento Grid across all 6 languages (`246` keys each) with crisp ClearType rendering and safe OneWay binding fixes.

## Download:
Download **SmoothFrame_Setup.exe** below and run the installer.
'@

$bodyObj = @{
    tag_name = "v2.3.0"
    target_commitish = "main"
    name = "SmoothFrame v2.3.0 - Process Memory Priority, Smart Focus & Hardware Inspector"
    body = $releaseNotes
    draft = $false
    prerelease = $false
}

$bodyJson = $bodyObj | ConvertTo-Json -Depth 5
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)

Write-Host "Creating GitHub Release for v2.3.0..."
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases" -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8"
    Write-Host "Created release: $($release.html_url)"
} catch {
    Write-Host "Checking if release already exists..."
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/Foxys1/SmoothFrame/releases/tags/v2.3.0" -Method Get -Headers $headers
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
