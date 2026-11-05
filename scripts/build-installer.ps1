<#
.SYNOPSIS
    Automated build and packaging script for Zenith Utility Windows Installer.
.DESCRIPTION
    1. Locates dotnet SDK and Inno Setup compiler.
    2. Builds and publishes self-contained win-x64 binaries.
    3. Compiles the modern Inno Setup installer.
    4. Computes SHA-256 verification hash.
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RootDir = (Resolve-Path "$ScriptDir\..").Path
$OutputDir = Join-Path $RootDir "build_output"
$ReleaseDir = Join-Path $RootDir "Release"
$InstallerScript = Join-Path $RootDir "installer\ZenithSetup.iss"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Zenith Utility - Installer Build Pipeline" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate dotnet executable
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetExe = ""
if ($dotnetCmd) {
    $dotnetExe = $dotnetCmd.Source
} else {
    $possibleDotnetPaths = @(
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
        "$env:ProgramFiles\dotnet\dotnet.exe"
    )
    foreach ($p in $possibleDotnetPaths) {
        if (Test-Path $p) {
            $dotnetExe = $p
            break
        }
    }
}

if (-not $dotnetExe -or -not (Test-Path $dotnetExe)) {
    Write-Error ".NET SDK (dotnet.exe) was not found. Please install .NET 8 SDK or add it to PATH."
}
Write-Host "[OK] Found .NET SDK: $dotnetExe" -ForegroundColor Green

# 2. Locate Inno Setup compiler (ISCC.exe)
$isccCmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$isccExe = ""
if ($isccCmd) {
    $isccExe = $isccCmd.Source
} else {
    $possibleIsccPaths = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $possibleIsccPaths) {
        if (Test-Path $p) {
            $isccExe = $p
            break
        }
    }
}

if (-not $isccExe -or -not (Test-Path $isccExe)) {
    Write-Error "Inno Setup compiler (ISCC.exe) was not found. Please install Inno Setup 6."
}
Write-Host "[OK] Found Inno Setup Compiler: $isccExe" -ForegroundColor Green

# 3. Optional test run
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "--> Running unit tests..." -ForegroundColor Yellow
    $testProj = Join-Path $RootDir "ZenithApp.Tests\ZenithApp.Tests.csproj"
    & $dotnetExe test $testProj -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed. Aborting installer build."
    }
    Write-Host "[OK] All tests passed successfully." -ForegroundColor Green
}

# 4. Publish self-contained single file
Write-Host ""
Write-Host "--> Publishing self-contained binaries to $OutputDir..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

$projPath = Join-Path $RootDir "ZenithApp\ZenithApp.csproj"
$publishArgs = @(
    "publish",
    $projPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $OutputDir
)

& $dotnetExe $publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish failed."
}
Write-Host "[OK] Self-contained application published." -ForegroundColor Green

# 5. Compile Inno Setup installer
Write-Host ""
Write-Host "--> Compiling Windows Installer with Inno Setup..." -ForegroundColor Yellow
if (-not (Test-Path $ReleaseDir)) {
    New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
}

& $isccExe $InstallerScript
if ($LASTEXITCODE -ne 0) {
    Write-Error "Inno Setup compilation failed."
}

# 6. Checksum generation
$installerFile = Get-ChildItem -Path $ReleaseDir -Filter "ZenithSetup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($installerFile) {
    Write-Host ""
    Write-Host "--> Generating SHA256 checksum..." -ForegroundColor Yellow
    $hash = (Get-FileHash -Path $installerFile.FullName -Algorithm SHA256).Hash
    $checksumPath = Join-Path $ReleaseDir "SHA256SUMS.txt"
    "$hash  $($installerFile.Name)" | Out-File -FilePath $checksumPath -Encoding utf8
    
    $sizeMb = [math]::Round($installerFile.Length / 1MB, 2)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host " BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host " Installer: $($installerFile.FullName)" -ForegroundColor Cyan
    Write-Host " Size:      $sizeMb MB" -ForegroundColor Cyan
    Write-Host " SHA256:    $hash" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Green
} else {
    Write-Warning "Could not find generated installer in $ReleaseDir"
}
