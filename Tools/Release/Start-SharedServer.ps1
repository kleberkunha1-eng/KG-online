param(
    [string]$BuildExe = (Join-Path $PSScriptRoot '..\..\Build\GameProjectKG.editorserver\GameProjectKG.exe')
)

$ErrorActionPreference = 'Stop'
$api = 'https://gamekg.pages.dev'
if (-not (Test-Path -LiteralPath $BuildExe -PathType Leaf)) {
    throw "Build Dedicated Server nao encontrada: $BuildExe. Nao usar a build publicada como fallback."
}
$health = Invoke-RestMethod -Uri "$api/api/health" -TimeoutSec 15
if ($health.status -ne 'API Online') { throw 'API Cloudflare indisponivel.' }
if (Get-NetUDPEndpoint -LocalPort 7777 -ErrorAction SilentlyContinue) {
    throw 'UDP 7777 ja esta em uso; nao iniciar uma segunda instancia.'
}
$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$log = Join-Path $logDirectory ('shared-server-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Write-Host "Servidor compartilhado UDP 7777; autenticacao/dados pela API $api."
Write-Host 'Este script nao inicia playit nem altera firewall. Contas e saves sao do Cloudflare.'
Write-Host "Log: $log. Ctrl+C encerra este servidor."
$server = Start-Process -FilePath $BuildExe -ArgumentList @(
    '-batchmode', '-nographics', '--server', '--server-port=7777',
    "--api=$api", '-logFile', "`"$log`""
) -PassThru
try {
    $server.WaitForExit()
    if ($server.ExitCode -ne 0) { throw "Servidor compartilhado encerrou com codigo $($server.ExitCode). Consulte $log." }
} finally {
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}
