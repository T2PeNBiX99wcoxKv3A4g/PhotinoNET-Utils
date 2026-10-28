#!/usr/bin/env pwsh
<#
.SYNOPSIS
Builds PhotinoNETUtils and copies its DLL and npm tarball into the configured projects.

.DESCRIPTION
The targets come from a JSON config file, so this script contains no project paths or names.

Create build-and-copy.config.json (git-ignored) from build-and-copy.config.example.json:

    {
      "libsDirectory": "Libs",
      "userInterfaceDirectory": "UserInterface",
      "targets": [
        { "name": "MyApp", "path": "../MyApp/MyApp" }
      ]
    }

Each target gets:

    <target>/<libsDirectory>/PhotinoNETUtils.dll
    <target>/<libsDirectory>/photino-net-utils-bridge-<version>.tgz

The "@photino-net-utils/bridge" dependency in <target>/<userInterfaceDirectory>/package.json
is updated to point at that tarball, unless that file does not exist.

.PARAMETER Config
Config file. Defaults to build-and-copy.config.json next to this script.

.PARAMETER SkipNpm
Do not rebuild/pack the npm package; copy the existing tarball.

.PARAMETER SkipTargetInstall
Do not run npm install in the target projects.

.EXAMPLE
pwsh ./build-and-copy.ps1

.EXAMPLE
pwsh ./build-and-copy.ps1 -Config ./my-targets.json -SkipTargetInstall
#>
[CmdletBinding()]
param(
    [string]$Config = (Join-Path $PSScriptRoot 'build-and-copy.config.json'),
    [switch]$SkipNpm,
    [switch]$SkipTargetInstall
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'PhotinoNETUtils\PhotinoNETUtils.csproj'
$ui = Join-Path $root 'UserInterface'
$npmManifest = Join-Path $ui 'package.json'
$readConfig = Join-Path $root 'scripts/read-config.mjs'
$setDependency = Join-Path $root 'scripts/set-bridge-dependency.mjs'

if (-not (Test-Path $project)) { throw "Project not found: $project" }
if (-not (Test-Path $npmManifest)) { throw "npm package not found: $ui" }
if (-not (Test-Path $Config)) {
    throw "Config not found: $Config. Copy build-and-copy.config.example.json to build-and-copy.config.json and edit it."
}

$resolvedConfig = (Resolve-Path $Config).Path
$rows = & node $readConfig $resolvedConfig
if ($LASTEXITCODE -ne 0) { throw "Could not read $resolvedConfig" }

$targets = foreach ($row in @($rows)) {
    $parts = $row -split "`t"
    [pscustomobject]@{
        Name = $parts[0]
        Path = $parts[1]
        Libs = $parts[2]
        Ui = $parts[3]
    }
}

if (-not $targets -or $targets.Count -eq 0) { throw 'The config has no targets.' }

$libraryVersion = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$npmVersion = (Get-Content $npmManifest -Raw | ConvertFrom-Json).version

if ($libraryVersion -ne $npmVersion) {
    Write-Warning "Version mismatch: library $libraryVersion, npm package $npmVersion"
}

Write-Host "Building PhotinoNETUtils $libraryVersion (Release)..." -ForegroundColor Cyan
dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }

$dll = Join-Path $root 'PhotinoNETUtils\bin\Release\net10.0\PhotinoNETUtils.dll'
if (-not (Test-Path $dll)) { throw "DLL not found: $dll" }
$dllHash = (Get-FileHash $dll -Algorithm SHA256).Hash

$tgzName = "photino-net-utils-bridge-$npmVersion.tgz"
$tgz = Join-Path $ui $tgzName

if (-not $SkipNpm) {
    if (-not (Test-Path (Join-Path $ui 'node_modules'))) {
        Write-Host 'Installing the npm package dependencies...' -ForegroundColor Cyan
        Push-Location $ui
        try { npm install --no-audit --no-fund } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
    }

    Write-Host "Packing $tgzName..." -ForegroundColor Cyan
    Push-Location $ui
    try { npm run pack } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'npm run pack failed' }
}

if (-not (Test-Path $tgz)) { throw "npm tarball not found: $tgz" }

foreach ($target in $targets) {
    if (-not (Test-Path $target.Path)) {
        Write-Warning "Skipping $($target.Name): $($target.Path) does not exist"
        continue
    }

    $libDirectory = Join-Path $target.Path $target.Libs
    New-Item -ItemType Directory -Force -Path $libDirectory | Out-Null

    $targetDll = Join-Path $libDirectory 'PhotinoNETUtils.dll'
    $targetHash = if (Test-Path $targetDll) { (Get-FileHash $targetDll -Algorithm SHA256).Hash } else { '' }
    if ($targetHash -ne $dllHash) { Copy-Item $dll $targetDll -Force }

    Get-ChildItem $libDirectory -Filter 'photino-net-utils-bridge-*.tgz' -File |
        Where-Object Name -ne $tgzName |
        Remove-Item -Force
    Copy-Item $tgz (Join-Path $libDirectory $tgzName) -Force

    $appUi = Join-Path $target.Path $target.Ui
    $appManifest = Join-Path $appUi 'package.json'

    if (Test-Path $appManifest) {
        & node $setDependency $appManifest $libDirectory $tgzName
        if ($LASTEXITCODE -ne 0) { throw "Could not update $appManifest" }

        if (-not $SkipTargetInstall) {
            $installed = Join-Path $appUi 'node_modules\@photino-net-utils\bridge'
            if (Test-Path $installed) { Remove-Item -Recurse -Force $installed }

            Write-Host "Installing the shared bridge in $appUi..." -ForegroundColor Cyan
            Push-Location $appUi
            try { npm install --no-audit --no-fund } finally { Pop-Location }
            if ($LASTEXITCODE -ne 0) { throw "npm install failed in $appUi" }
        }
    }

    Write-Host "Copied to $libDirectory" -ForegroundColor Green
}

Write-Host 'Done.' -ForegroundColor Green
