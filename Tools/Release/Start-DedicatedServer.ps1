<#
.SYNOPSIS
    Mantem o servidor dedicado do jogo (Mirror/KCP) rodando, reiniciando automaticamente se
    o processo cair.

.DESCRIPTION
    O tunel playit.gg roda separado, como SERVICO do Windows (instalado pelo instalador oficial
    do playit.gg, ja configurado para iniciar automaticamente com o Windows - veja
    "Get-Service playitd"). Este script cuida apenas do processo do JOGO em si.

    Por que playit.gg: ele cria um tunel de SAIDA da sua maquina ate a rede deles, entao voce
    nao precisa abrir nenhuma porta no roteador nem expor seu IP residencial na internet. Quem
    joga conecta no endereco publico do playit (ex.: algumacoisa.joinmc.link:25565), que
    encaminha o trafego ate esta maquina.

    Pre-requisitos (feitos uma unica vez, no painel web https://playit.gg/account, porque exigem
    login na sua conta):
    1. Confirmar o e-mail da conta (o playit nao expõe tuneis criados por contas nao verificadas).
    2. Dashboard -> Tunnels -> Add Tunnel -> protocolo UDP -> porta local 7777 (ou a que voce
       escolher abaixo) -> associar ao agente desta maquina.
    3. O playit vai gerar um endereco publico tipo "algumacoisa.joinmc.link:PORTA_PUBLICA".
       Cole esse endereco em Tools\server-address.txt (1a linha, formato host:porta) antes de
       gerar a build do jogo - e assim que o cliente publicado sabe para onde conectar.

.PARAMETER ServerPort
    Porta local do servidor dedicado do jogo (KCP/UDP). Deve ser a MESMA porta local configurada
    no tunel do playit.gg. Padrao: 7777.

.PARAMETER BuildExe
    Caminho do executavel do jogo (a mesma build do jogador, rodada aqui com --server).

.EXAMPLE
    .\Start-DedicatedServer.ps1
#>
param(
    [int]$ServerPort = 7777,
    [string]$BuildExe = (Join-Path $PSScriptRoot "..\..\Build\GameProjectKG\GameProjectKG.exe")
)

$ErrorActionPreference = "Stop"

# Avisa (sem travar) se o servico do playit.gg nao estiver rodando - o servidor do jogo ainda
# funciona localmente, mas ninguem de fora vai conseguir conectar sem o tunel ativo.
$playitSvc = Get-Service -Name "playitd" -ErrorAction SilentlyContinue
if (-not $playitSvc) {
    Write-Warning "Servico 'playitd' (playit.gg) nao encontrado nesta maquina. Instale o playit.gg em https://playit.gg/download antes de publicar o endereco para os jogadores."
}
elseif ($playitSvc.Status -ne "Running") {
    Write-Warning "Servico 'playitd' (playit.gg) esta '$($playitSvc.Status)', nao 'Running'. Iniciando..."
    Start-Service -Name "playitd" -ErrorAction SilentlyContinue
}
else {
    Write-Host "Tunel playit.gg: servico ativo (OK)." -ForegroundColor Green
}

if (-not (Test-Path $BuildExe)) {
    Write-Error "Build do jogo nao encontrada em $BuildExe. Gere a build primeiro (Unity: Tools > Build Game (Windows))."
    exit 1
}

$logDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$serverLog = Join-Path $logDir "server"
$serverArgs = @("-batchmode", "-nographics", "--server", "--server-port=$ServerPort")

Write-Host "Iniciando servidor dedicado na porta local $ServerPort. Ctrl+C para parar." -ForegroundColor Green
Write-Host "Logs em: $logDir" -ForegroundColor DarkGray

try {
    while ($true) {
        $serverProc = Start-Process -FilePath $BuildExe -ArgumentList $serverArgs `
            -RedirectStandardOutput "$serverLog.out.log" -RedirectStandardError "$serverLog.err.log" `
            -WindowStyle Hidden -PassThru

        Write-Host "[$(Get-Date -Format HH:mm:ss)] Servidor iniciado, PID $($serverProc.Id)" -ForegroundColor Cyan
        $serverProc.WaitForExit()

        Write-Host "[$(Get-Date -Format HH:mm:ss)] Servidor caiu (codigo $($serverProc.ExitCode)). Reiniciando em 5s..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
    }
}
finally {
    if ($serverProc -and -not $serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue }
}
