param([string]$RepoRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
foreach ($project in @('DATA_SCRAPPER\NaverKboRelayUI.csproj','PC\src\NaverRelay.Cli\NaverRelay.Cli.csproj','WEB\server\src\NaverSabermetrics.Web\NaverSabermetrics.Web.csproj')) {
    & dotnet build (Join-Path $RepoRoot $project) -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패: $project" }
}
$remoteToolDir = Join-Path $PSScriptRoot 'remote-tools\linux-x64'
& dotnet publish (Join-Path $RepoRoot 'PC\src\NaverRelay.Cli\NaverRelay.Cli.csproj') -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $remoteToolDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Linux 원격 임포터 publish 실패' }
