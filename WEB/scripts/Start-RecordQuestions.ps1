param([string]$DatabasePath = "$env:LOCALAPPDATA\NaverSabermetrics\Data\sabermetrics_v2.db", [int]$Port = 5128)
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot '../server/src/NaverSabermetrics.Web/NaverSabermetrics.Web.csproj'
$output=Join-Path $env:TEMP 'Fanzai-RecordQuestions'
dotnet build $project -o $output -v minimal
if($LASTEXITCODE -ne 0){throw 'Build failed'}
if(-not (Test-Path -LiteralPath $DatabasePath)){throw 'DB file not found'}
# Secret is entered interactively; never put it in command history, a file, or Git.
$secret=Read-Host 'OpenAI API key (input hidden)' -AsSecureString
$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
$previousKey=$env:OPENAI_API_KEY
$previousEnvironment=$env:ASPNETCORE_ENVIRONMENT
$previousRender=$env:RENDER
$previousBotKey=$env:FANZAI_BOT_API_KEY
try {
    $botKeyPath=Join-Path $output 'fanzai-bot-key.txt'
    if(-not $previousBotKey){
        if(-not (Test-Path -LiteralPath $botKeyPath)){
            $keyBytes=New-Object byte[] 32
            $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
            try{$rng.GetBytes($keyBytes)}finally{$rng.Dispose()}
            [IO.File]::WriteAllText($botKeyPath,[Convert]::ToBase64String($keyBytes))
        }
        $env:FANZAI_BOT_API_KEY=[IO.File]::ReadAllText($botKeyPath).Trim()
        Write-Host "Bot shared key file (private): $botKeyPath"
    }
    $env:OPENAI_API_KEY=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:RENDER='false'
    Write-Host "Bot API: http://127.0.0.1:$Port/api/bot/ask (web question UI hidden)"
    dotnet (Join-Path $output 'NaverSabermetrics.Web.dll') --urls "http://127.0.0.1:$Port" --Site:DatabasePath $DatabasePath --Site:StateDirectory (Join-Path $output 'state') --Site:AllowDevelopmentGuest true --Logging:EventLog:LogLevel:Default None --RecordQuestions:Enabled true --RecordQuestions:DailyLimit 10000 --RecordQuestions:PerIpDailyLimit 1000 --BotStarters:Enabled false
} finally {
    $env:OPENAI_API_KEY=$previousKey;$env:ASPNETCORE_ENVIRONMENT=$previousEnvironment;$env:RENDER=$previousRender
    $env:FANZAI_BOT_API_KEY=$previousBotKey
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $secret.Dispose()
}
