param(
    [string]$LogDir = "$env:USERPROFILE\Documents\NaverKboCombined\logs",
    [ValidateRange(1,10)][int]$PollSeconds = 2
)

$ErrorActionPreference = 'Stop'
try {
    [Console]::OutputEncoding = [Text.Encoding]::UTF8
    $OutputEncoding = [Text.Encoding]::UTF8
} catch {}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
Write-Host "KBO 자동화 로그 감시 중: $LogDir"
Write-Host '이 창은 자동으로 닫히지 않습니다. 끝내려면 Ctrl+C를 누르세요.'

$currentPath = $null
[long]$position = 0
while ($true) {
    $latest = Get-ChildItem -LiteralPath $LogDir -File -Filter 'pipeline_*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) {
        if ($latest.FullName -ne $currentPath) {
            $currentPath = $latest.FullName
            $position = 0
            Write-Host ''
            Write-Host "===== $($latest.Name) ====="
        }
        $stream = [IO.File]::Open($currentPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            if ($stream.Length -lt $position) { $position = 0 }
            [void]$stream.Seek($position,[IO.SeekOrigin]::Begin)
            $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
            try {
                while (!$reader.EndOfStream) { Write-Host $reader.ReadLine() }
                $position = $stream.Position
            } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    }
    Start-Sleep -Seconds $PollSeconds
}
