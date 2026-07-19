[CmdletBinding()]
param(
    [ValidateSet('Validate', 'BuildRelease', 'PackageRelease', 'Clean')]
    [string]$Action = 'Validate',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$releaseRoot = Join-Path $workspaceRoot 'release\ScreenshotCat_Windows\release'
$publishRoot = Join-Path $releaseRoot 'App'
$project = Join-Path $projectRoot 'ScreenshotCat\ScreenshotCat.csproj'
$verification = Join-Path $projectRoot 'ScreenshotCat.Verification\ScreenshotCat.Verification.csproj'

$sdkRoot = Join-Path $workspaceRoot 'sdk'
$env:DOTNET_CLI_HOME = Join-Path $sdkRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $sdkRoot 'nuget-packages'
$env:TEMP = Join-Path $workspaceRoot 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:TEMP | Out-Null

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code ${LASTEXITCODE}: dotnet $($Arguments -join ' ')"
    }
}

function Assert-ReleasePath {
    param([Parameter(Mandatory)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $expected = [System.IO.Path]::GetFullPath($releaseRoot)
    if (-not $fullPath.StartsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside release root: $fullPath"
    }
}

function Invoke-Validation {
    Invoke-Dotnet @('build', $project, '--configuration', 'Release', '--runtime', 'win-x64', "-p:Version=$Version")
    Invoke-Dotnet @('run', '--project', $verification, '--configuration', 'Release', '--runtime', 'win-x64', "-p:Version=$Version")
}

function Invoke-ReleaseBuild {
    Assert-ReleasePath $publishRoot
    if (Test-Path -LiteralPath $publishRoot) {
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
    Invoke-Dotnet @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishRoot,
        "-p:Version=$Version",
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        '-p:WindowsAppSDKSelfContained=true',
        '-p:WindowsPackageType=None'
    )

    # WinUI's unpackaged publish target can omit compiled XAML/PRI resources.
    # Copy the exact resources produced by the matching Release build so the
    # published executable can resolve App.xaml and every window at runtime.
    $pri = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'ScreenshotCat\bin\Release') `
        -Filter 'ScreenshotCat.pri' -Recurse -File |
        Where-Object { $_.DirectoryName.EndsWith('\win-x64', [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $pri) {
        throw 'Release build did not produce ScreenshotCat.pri.'
    }
    Copy-Item -LiteralPath $pri.FullName -Destination $publishRoot
    Get-ChildItem -LiteralPath $pri.DirectoryName -Filter '*.xbf' -File |
        Copy-Item -Destination $publishRoot

    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $publishRoot
    $docsAssets = Join-Path $publishRoot 'assets'
    New-Item -ItemType Directory -Force -Path $docsAssets | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\sponsor.jpg') -Destination $docsAssets
}

function New-ReleasePackage {
    $archive = Join-Path $releaseRoot "ScreenshotCat-v$Version-win-x64.zip"
    $checksum = "$archive.sha256"
    Assert-ReleasePath $archive
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $archive)" | Set-Content -LiteralPath $checksum -Encoding ascii
    [pscustomobject]@{
        Version = $Version
        Archive = $archive
        Bytes = (Get-Item -LiteralPath $archive).Length
        SHA256 = $hash
    } | ConvertTo-Json
}

function Clear-BuildCache {
    $targets = @(
        (Join-Path $projectRoot 'ScreenshotCat\bin'),
        (Join-Path $projectRoot 'ScreenshotCat\obj'),
        (Join-Path $projectRoot 'ScreenshotCat.Verification\bin'),
        (Join-Path $projectRoot 'ScreenshotCat.Verification\obj')
    )
    foreach ($target in $targets) {
        $fullPath = [System.IO.Path]::GetFullPath($target)
        if (-not $fullPath.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean path outside project root: $fullPath"
        }
        if (Test-Path -LiteralPath $fullPath) {
            Remove-Item -LiteralPath $fullPath -Recurse -Force
        }
    }
}

switch ($Action) {
    'Validate' {
        Invoke-Validation
    }
    'BuildRelease' {
        Invoke-ReleaseBuild
    }
    'PackageRelease' {
        Invoke-Validation
        Invoke-ReleaseBuild
        New-ReleasePackage
    }
    'Clean' {
        Clear-BuildCache
    }
}
