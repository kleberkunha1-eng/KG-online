param(
    [string]$BuildExe = (Join-Path $PSScriptRoot '..\..\Build\GameProjectKG.editorserver\GameProjectKG.exe')
)

$ErrorActionPreference = 'Stop'
$api = 'http://127.0.0.1:3000'
if (-not (Test-Path -LiteralPath $BuildExe -PathType Leaf)) {
    throw "Build de teste nao encontrada: $BuildExe. A build publicada nao sera usada."
}
$health = Invoke-RestMethod -Uri "$api/api/health" -TimeoutSec 5
if ($health.status -ne 'API Online') {
    throw 'API local indisponivel. Inicie TOP-MariaDB12 e API\start-local.ps1 primeiro.'
}
if (Get-NetUDPEndpoint -LocalPort 7778 -ErrorAction SilentlyContinue) {
    throw 'UDP 7778 ja esta em uso. Verifique o servidor existente antes de iniciar outro.'
}

$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$log = Join-Path $logDirectory ('editor-server-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Write-Host "Servidor de teste UDP 7778; API $api. Sem playit ou alteracoes de firewall."
Write-Host "Log: $log. Ctrl+C encerra somente este servidor."
$server = Start-Process -FilePath $BuildExe -ArgumentList @(
    '-batchmode', '-nographics', '--server', '--server-port=7778',
    "--api=$api", '-logFile', "`"$log`""
) -PassThru
try {
    $server.WaitForExit()
    if ($server.ExitCode -ne 0) {
        throw "Servidor de teste encerrou com codigo $($server.ExitCode). Consulte $log."
    }
} finally {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id
    }
}
