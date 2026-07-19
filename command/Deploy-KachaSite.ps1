[CmdletBinding()]
param(
    [ValidateSet('Stage', 'Validate', 'Deploy', 'Status')]
    [string]$Action = 'Validate'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$websiteRoot = Join-Path $projectRoot 'website'
$stageRoot = Join-Path $websiteRoot '.dist'
$configPath = Join-Path $projectRoot 'wrangler.jsonc'
$expectedSponsorHash = '1E23933B0C5DA7169FFBBC64EF58B324867ADA4EA38CF1F772F2CF13BA5C300A'
$expectedArchiveHash = '7c00720a30c1fa9dfc333e737af1881551620e39e3a365acf761e672204c52af'
$downloadUrl = 'https://github.com/nekobyran/ScreenshotCat/releases/download/v1.0.0/ScreenshotCat-v1.0.0-win-x64.zip'
$siteUrl = 'https://kacha.nkbr.cc/'
$publicFiles = @('index.html', '404.html', 'styles.css', 'script.js', 'robots.txt', '_headers', 'assets\app-icon.webp', 'assets\sponsor.jpg')

function Assert-StagePath {
    $full = [System.IO.Path]::GetFullPath($stageRoot)
    $expected = [System.IO.Path]::GetFullPath((Join-Path $websiteRoot '.dist'))
    if (-not [string]::Equals($full, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unexpected stage path: $full"
    }
}

function Invoke-Stage {
    Assert-StagePath
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
    foreach ($relative in $publicFiles) {
        $source = Join-Path $websiteRoot $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Missing public file: $relative"
        }
        $target = Join-Path $stageRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
    "stage=pass;files=$($publicFiles.Count);path=$stageRoot"
}

function Invoke-Validate {
    $indexPath = Join-Path $websiteRoot 'index.html'
    $index = Get-Content -LiteralPath $indexPath -Raw -Encoding utf8
    $script = Get-Content -LiteralPath (Join-Path $websiteRoot 'script.js') -Raw -Encoding utf8
    $headers = Get-Content -LiteralPath (Join-Path $websiteRoot '_headers') -Raw -Encoding utf8
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding utf8 | ConvertFrom-Json

    foreach ($required in @(
        'Kacha — Windows 截图与批注工具',
        'v1.0.0 已上线',
        $downloadUrl,
        'https://github.com/nekobyran/ScreenshotCat',
        $expectedArchiveHash,
        'Windows 10 1809+',
        '完全本地处理',
        'assets/sponsor.jpg'
    )) {
        if (-not $index.Contains($required, [System.StringComparison]::Ordinal)) {
            throw "Release page missing required content: $required"
        }
    }
    if (([regex]::Matches($index, [regex]::Escape($downloadUrl))).Count -ne 2) {
        throw 'Release page must expose exactly two primary download links.'
    }
    if ($index -match '(?i)(api[_-]?key|access[_-]?token|client[_-]?secret|password)\s*[:=]') {
        throw 'Release page contains a possible secret field.'
    }
    if ($script -match '(?i)(eval\s*\(|innerHTML\s*=|document\.write\s*\()') {
        throw 'Release page script uses a forbidden dynamic HTML primitive.'
    }
    if ($headers -notmatch 'Content-Security-Policy:' -or $headers -notmatch 'Strict-Transport-Security:') {
        throw 'Static headers must include CSP and HSTS.'
    }
    if ($config.name -ne 'kacha-release' -or $config.assets.directory -ne './website/.dist') {
        throw 'Wrangler static asset project configuration is incorrect.'
    }
    if ($config.routes.Count -ne 1 -or $config.routes[0].pattern -ne 'kacha.nkbr.cc' -or -not $config.routes[0].custom_domain) {
        throw 'Wrangler custom domain configuration is incorrect.'
    }
    $sponsor = Join-Path $websiteRoot 'assets\sponsor.jpg'
    if ((Get-FileHash -LiteralPath $sponsor -Algorithm SHA256).Hash -cne $expectedSponsorHash) {
        throw 'Sponsor image hash does not match the verified source.'
    }
    foreach ($relative in $publicFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $websiteRoot $relative) -PathType Leaf)) {
            throw "Missing public file: $relative"
        }
    }
    'validation=pass;domain=kacha.nkbr.cc;version=1.0.0;downloads=2;security-headers=pass;sponsor=verified'
}

function Invoke-Wrangler {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $npmCache = Join-Path $workspaceRoot 'cache\npm'
    New-Item -ItemType Directory -Force -Path $npmCache | Out-Null
    $env:npm_config_cache = $npmCache
    $npx = (Get-Command 'npx.cmd' -ErrorAction Stop).Source
    & $npx --yes 'wrangler@4.112.0' @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Wrangler failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Invoke-Status {
    $response = Invoke-WebRequest -Uri $siteUrl -TimeoutSec 30
    if ($response.StatusCode -ne 200 -or -not $response.Content.Contains('v1.0.0 已上线')) {
        throw 'Production site did not return the expected release page.'
    }
    foreach ($header in @('Content-Security-Policy', 'Strict-Transport-Security', 'X-Content-Type-Options')) {
        if (-not $response.Headers.ContainsKey($header)) {
            throw "Production response is missing security header: $header"
        }
    }
    $asset = Invoke-WebRequest -Uri ("${siteUrl}assets/app-icon.webp") -Method Head -TimeoutSec 30
    if ($asset.StatusCode -ne 200 -or $asset.Headers.'Content-Type' -notmatch 'image/webp') {
        throw 'Production app icon is unavailable or has the wrong MIME type.'
    }
    $download = Invoke-WebRequest -Uri $downloadUrl -Method Head -MaximumRedirection 8 -TimeoutSec 60
    if ($download.StatusCode -ne 200) {
        throw 'GitHub release asset is unavailable.'
    }
    "status=pass;url=$siteUrl;http=200;security-headers=pass;download=200"
}

switch ($Action) {
    'Stage' { Invoke-Stage }
    'Validate' { Invoke-Validate }
    'Deploy' {
        Invoke-Stage
        Invoke-Validate
        Invoke-Wrangler @('whoami')
        Invoke-Wrangler @('deploy', '--config', $configPath)
    }
    'Status' { Invoke-Status }
}
