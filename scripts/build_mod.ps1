<#
.SYNOPSIS
    Builds and packages the SmoothFrame NeoForge 1.21.1 In-Game Optimization Mod.
.DESCRIPTION
    Uses host Java 21 JDK (javac, jar) and local NeoForge 1.21.1 libraries to compile
    the mod sources and package SmoothFrameMod-1.21.1.jar into both mods/ and src/SmoothFrame.UI/Mods/.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "",
    [string]$UiModsDir = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\mods"))
}
if ([string]::IsNullOrWhiteSpace($UiModsDir)) {
    $UiModsDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\SmoothFrame.UI\Mods"))
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "SmoothFrame NeoForge 1.21.1 Mod Build & Packaging Pipeline" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 1. Resolve JDK Tools (javac and jar)
$javacCmd = Get-Command javac.exe -ErrorAction SilentlyContinue
$jarCmd = Get-Command jar.exe -ErrorAction SilentlyContinue

if (-not $javacCmd -or -not $jarCmd) {
    # Fallback to standard Adoptium JDK paths
    $programFiles = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ProgramFiles)
    $adoptiumDir = Join-Path $programFiles "Eclipse Adoptium"
    if (Test-Path $adoptiumDir) {
        $jdkBins = Get-ChildItem -Path $adoptiumDir -Filter "javac.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($jdkBins) {
            $binPath = Split-Path $jdkBins.FullName
            $env:PATH = "$binPath;$env:PATH"
            $javacCmd = Get-Command javac.exe -ErrorAction SilentlyContinue
            $jarCmd = Get-Command jar.exe -ErrorAction SilentlyContinue
        }
    }
}

if (-not $javacCmd) {
    throw "javac.exe could not be located in PATH or Eclipse Adoptium directory."
}
if (-not $jarCmd) {
    throw "jar.exe could not be located in PATH or Eclipse Adoptium directory."
}

$javac = $javacCmd.Source
$jar = $jarCmd.Source
Write-Host "[JDK] Using javac: $javac" -ForegroundColor Gray
Write-Host "[JDK] Using jar:   $jar" -ForegroundColor Gray

# 2. Resolve NeoForge 1.21.1 Local Libraries
$appData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)
$libCandidates = @(
    $env:MINECRAFT_LIBS,
    (Join-Path $appData ".minecraft\libraries"),
    (Join-Path $appData ".tlauncher\legacy\Minecraft\game\libraries")
)
$libDir = $libCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $libDir -or -not (Test-Path $libDir)) {
    throw "Local Minecraft libraries cache directory not found in .minecraft or .tlauncher."
}

$requiredJars = @(
    "net\neoforged\bus\8.0.5\bus-8.0.5.jar",
    "net\neoforged\fancymodloader\loader\4.0.45\loader-4.0.45.jar",
    "net\neoforged\mergetool\2.0.0\mergetool-2.0.0-api.jar",
    "net\neoforged\neoforge\21.1.265\neoforge-21.1.265-universal.jar",
    "net\neoforged\neoforge\21.1.265\neoforge-21.1.265-client.jar",
    "net\minecraft\client\1.21.1-20240808.144430\client-1.21.1-20240808.144430-srg.jar",
    "org\joml\joml\1.10.8\joml-1.10.8.jar"
)

$classpathEntries = [System.Collections.Generic.List[string]]::new()
foreach ($relJar in $requiredJars) {
    $fullPath = Join-Path $libDir $relJar
    if (-not (Test-Path $fullPath)) {
        throw "Required NeoForge dependency not found: $fullPath"
    }
    $classpathEntries.Add($fullPath)
}
$classpath = $classpathEntries -join ';'
Write-Host "[Classpath] Resolved $($classpathEntries.Count) NeoForge 1.21.1 runtime JARs." -ForegroundColor Gray

# 3. Collect Java Source Files
$srcDir = Join-Path $PSScriptRoot "..\src\smoothframe-mod\java"
$resDir = Join-Path $PSScriptRoot "..\src\smoothframe-mod\resources"

if (-not (Test-Path $srcDir)) {
    throw "Java source directory not found: $srcDir"
}

$srcFiles = Get-ChildItem -Path $srcDir -Filter "*.java" -Recurse | Select-Object -ExpandProperty FullName
if ($srcFiles.Count -eq 0) {
    throw "No .java source files found in $srcDir"
}
Write-Host "[Sources] Found $($srcFiles.Count) Java source files to compile." -ForegroundColor Gray

# 4. Compile Mod Classes to Temporary Directory
$tempBinDir = Join-Path $PSScriptRoot "..\bin\mod_build"
if (Test-Path $tempBinDir) {
    Remove-Item $tempBinDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempBinDir -Force | Out-Null

Write-Host "[Build] Compiling mod with javac..." -ForegroundColor Yellow
& $javac -cp $classpath -d $tempBinDir $srcFiles
if ($LASTEXITCODE -ne 0) {
    throw "javac compilation failed with exit code $LASTEXITCODE."
}
Write-Host "[Build] Compilation succeeded with 0 errors." -ForegroundColor Green

# 5. Copy Mod Manifest and Resources
$metaInfSrc = Join-Path $resDir "META-INF\neoforge.mods.toml"
if (-not (Test-Path $metaInfSrc)) {
    throw "Mod manifest not found: $metaInfSrc"
}
$metaInfDestDir = Join-Path $tempBinDir "META-INF"
New-Item -ItemType Directory -Path $metaInfDestDir -Force | Out-Null
Copy-Item -Path $metaInfSrc -Destination (Join-Path $metaInfDestDir "neoforge.mods.toml") -Force
Write-Host "[Manifest] Copied META-INF\neoforge.mods.toml into packaging tree." -ForegroundColor Gray

# 6. Package Mod JAR
$jarName = "SmoothFrameMod-1.21.1.jar"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$rootJarTarget = Join-Path $OutputDir $jarName

Write-Host "[Packaging] Creating $jarName archive..." -ForegroundColor Yellow
& $jar -cvf $rootJarTarget -C $tempBinDir .
if ($LASTEXITCODE -ne 0) {
    throw "jar packaging failed with exit code $LASTEXITCODE."
}

# 7. Bundle into SmoothFrame.UI Project
if (-not (Test-Path $UiModsDir)) {
    New-Item -ItemType Directory -Path $UiModsDir -Force | Out-Null
}
$uiJarTarget = Join-Path $UiModsDir $jarName
Copy-Item -Path $rootJarTarget -Destination $uiJarTarget -Force

# 8. Clean Up Temporary Build Output
Remove-Item -Path $tempBinDir -Recurse -Force -ErrorAction SilentlyContinue
$parentBin = Split-Path $tempBinDir -Parent
if ((Test-Path $parentBin) -and @(Get-ChildItem -Path $parentBin -Force).Count -eq 0) {
    Remove-Item -Path $parentBin -Force -ErrorAction SilentlyContinue
}

# 9. Verify Generated Artifacts
$rootJarItem = Get-Item $rootJarTarget
$uiJarItem = Get-Item $uiJarTarget

Write-Host "============================================================" -ForegroundColor Green
Write-Host "BUILD SUCCESSFUL" -ForegroundColor Green
Write-Host "Output 1: $($rootJarItem.FullName) ($($rootJarItem.Length) bytes)" -ForegroundColor Green
Write-Host "Output 2: $($uiJarItem.FullName) ($($uiJarItem.Length) bytes)" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
