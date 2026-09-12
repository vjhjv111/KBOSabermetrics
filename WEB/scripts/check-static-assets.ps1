[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$project = Join-Path $ProjectRoot 'server\src\NaverSabermetrics.Web'
$output = Join-Path $project "bin\$Configuration\net8.0"
$source = Join-Path $ProjectRoot 'frontend'
foreach ($asset in @('index.html','app.css','app.js','games.js','diamond.js','analysis.js','analysis.css')) {
    $original = Join-Path $source $asset
    if (!(Test-Path -LiteralPath $original -PathType Leaf)) { throw "Missing frontend source: $original" }
    $expected = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
    foreach ($dir in @((Join-Path $project 'wwwroot'), (Join-Path $output 'wwwroot'))) {
        $file = Join-Path $dir $asset
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing static asset: $file. Rebuild the solution." }
        if ((Get-Item -LiteralPath $file).Length -eq 0) { throw "Empty static asset: $file" }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) {
            throw "Static asset differs from frontend source: $file. Rebuild the solution."
        }
        Write-Output "PASS: static asset $file"
    }
}
# In Development the SDK runtime manifest may reference source directories even
# when Program.cs explicitly selects bin/.../wwwroot as the runtime web root.
$manifestPath = Join-Path $output 'NaverSabermetrics.Web.staticwebassets.runtime.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($contentRoot in @($manifest.ContentRoots)) {
        if ([string]::IsNullOrWhiteSpace($contentRoot) -or !(Test-Path -LiteralPath $contentRoot -PathType Container)) {
            throw "Static assets manifest references a missing directory: $contentRoot. Clean/rebuild in the CURRENT source folder."
        }
        Write-Output "PASS: manifest content root $contentRoot"
    }
}
Write-Output 'STATIC_ASSETS_CHECK_PASS'
