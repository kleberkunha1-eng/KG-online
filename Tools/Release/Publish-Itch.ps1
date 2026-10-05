<#
.SYNOPSIS
    Publica (ou atualiza) a build do jogo no itch.io usando o Butler, a ferramenta oficial
    de linha de comando do itch.io.

.DESCRIPTION
    Diferente do launcher proprio (que precisa baixar arquivos soltos do GitHub Releases e
    sofre com antivirus alterando/corrompendo esses arquivos), o app itch.io ja e uma aplicacao
    confiavel e reconhecida pela maioria dos antivirus - ele cuida do download, instalacao e
    atualizacao automatica dos jogadores, sem voce precisar resolver esse problema sozinho.

    Este script:
    1. Garante que o Butler esta baixado em Tools\Release\butler\butler.exe (baixa automaticamente
       na primeira execucao se nao existir).
    2. Verifica se voce ja fez login (butler login) - se nao, pede pra voce fazer isso uma vez
       (abre o navegador, e so autorizar a conta itch.io).
    3. Envia a pasta de build para o canal indicado (ex.: windows).

.PARAMETER BuildDir
    Pasta com a build do jogo pronta (ex.: Build\GameProjectKG, gerada pelo Unity em
    Tools > Build Game (Windows)).

.PARAMETER ItchUser
    Seu nome de usuario no itch.io (o que aparece na URL do seu perfil, ex.: kleberkunha).

.PARAMETER ItchGame
    O "slug" do jogo no itch.io (o que aparece na URL da pagina do jogo, ex.: gameprojectkg).

.PARAMETER Channel
    Canal do Butler (separa builds por plataforma/variante). Padrao: windows.

.EXAMPLE
    .\Publish-Itch.ps1 -BuildDir "..\..\Build\GameProjectKG" -ItchUser "kleberkunha" -ItchGame "gameprojectkg"
#>
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot "..\..\Build\GameProjectKG"),
    [Parameter(Mandatory = $true)][string]$ItchUser,
    [Parameter(Mandatory = $true)][string]$ItchGame,
    [string]$Channel = "windows"
)

$ErrorActionPreference = "Stop"

$butlerDir = Join-Path $PSScriptRoot "butler"
$butlerExe = Join-Path $butlerDir "butler.exe"

if (-not (Test-Path $BuildDir)) {
    Write-Error "Pasta de build nao encontrada: $BuildDir. Gere a build primeiro (Unity: Tools > Build Game (Windows))."
    exit 1
}

if (-not (Test-Path $butlerExe)) {
    Write-Host "Butler nao encontrado - baixando (uma vez so, ~15MB)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $butlerDir -Force | Out-Null
    $zipPath = Join-Path $butlerDir "butler.zip"
    Invoke-WebRequest -Uri "https://broth.itch.zone/butler/windows-amd64/LATEST/archive/default" -OutFile $zipPath
    Expand-Archive -Path $zipPath -DestinationPath $butlerDir -Force
    Remove-Item $zipPath -Force
    if (-not (Test-Path $butlerExe)) {
        Write-Error "Falha ao baixar o Butler. Baixe manualmente em https://itch.io/docs/butler/ e coloque em $butlerExe"
        exit 1
    }
    Write-Host "Butler baixado em $butlerExe" -ForegroundColor Green
}

# butler status falha se nao houver login salvo - usamos isso so pra avisar o usuario com uma
# mensagem clara, sem tentar automatizar o login (que abre o navegador e exige autorizacao manual
# da conta, nao da pra automatizar isso com seguranca).
$statusCheck = & $butlerExe status "$ItchUser/$ItchGame`:$Channel" 2>&1
if ($LASTEXITCODE -ne 0 -and ($statusCheck -join "`n") -match "authentication|credentials|login") {
    Write-Host ""
    Write-Host "Voce ainda nao fez login no Butler nesta maquina. Rode isto uma vez (abre o navegador):" -ForegroundColor Yellow
    Write-Host "  $butlerExe login" -ForegroundColor Yellow
    Write-Host "Depois rode este script de novo." -ForegroundColor Yellow
    exit 1
}

Write-Host "Enviando '$BuildDir' para $ItchUser/$ItchGame`:$Channel ..." -ForegroundColor Cyan
& $butlerExe push $BuildDir "$ItchUser/$ItchGame`:$Channel"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao publicar no itch.io (codigo $LASTEXITCODE)."
    exit 1
}

Write-Host ""
Write-Host "Publicado com sucesso! Os jogadores que usam o app itch.io recebem a atualizacao automaticamente." -ForegroundColor Green
