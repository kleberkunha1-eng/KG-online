#Requires -Version 5.1
<#
.SYNOPSIS
    Publica uma atualizacao do jogo nos dois canais de distribuicao de uma vez: GitHub Releases
    (usado pelo launcher proprio) e itch.io (usado pelo app/zip do itch.io).

.DESCRIPTION
    Fluxo:
      1. (Opcional, padrao: sim) Gera a build do jogo no Unity via linha de comando
         (Tools/build.request + GameBuild.cs), que ja inclui o api.json correto.
      2. Publica a build em GitHub Releases + atualiza o manifest.json do launcher
         (Launcher/tools/publish.js), igual ao fluxo manual de sempre.
      3. Publica a mesma build no itch.io via Butler (Publish-Itch.ps1).

    Use -SkipUnityBuild se ja gerou a build manualmente no Editor do Unity (Tools > Build Game).
    Use -SkipGithub ou -SkipItch para publicar em so um dos dois canais.

.PARAMETER Notes
    Texto das notas de versao (aparece no launcher e no log do GitHub Release).

.PARAMETER ItchUser
    Usuario do itch.io. Padrao: kg-online.

.PARAMETER ItchGame
    Slug do jogo no itch.io. Padrao: kg-online.

.EXAMPLE
    .\Publish-Update.ps1 -Notes "Correcao de bugs e novos itens"
#>
param(
    [string]$Notes = "Atualizacao",
    [string]$ItchUser = "kg-online",
    [string]$ItchGame = "kg-online",
    [string]$Channel = "windows",
    [switch]$SkipUnityBuild,
    [switch]$SkipGithub,
    [switch]$SkipItch
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$buildDir = Join-Path $root "Build\GameProjectKG"

function Step($name) { Write-Host ""; Write-Host "==> $name" -ForegroundColor Cyan }

# 1. Build do Unity -----------------------------------------------------------------
if (-not $SkipUnityBuild) {
    Step "Gerando build do Unity (Tools > Build Game)"
    $unityExe = Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Filter "Unity.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $unityExe) {
        Write-Warning "Unity.exe nao encontrado automaticamente. Gere a build manualmente no Editor (Tools > Build Game (Windows)) e rode este script com -SkipUnityBuild."
        exit 1
    }
    $requestFile = Join-Path $root "Tools\build.request"
    $resultFile = Join-Path $root "Tools\build-result.txt"
    if (Test-Path $resultFile) { Remove-Item $resultFile -Force }
    New-Item -ItemType File -Path $requestFile -Force | Out-Null
    Write-Host "Aguardando o Unity (precisa estar aberto neste projeto) processar o pedido de build..." -ForegroundColor Yellow
    $timeout = (Get-Date).AddMinutes(10)
    while (-not (Test-Path $resultFile) -and (Get-Date) -lt $timeout) { Start-Sleep -Seconds 3 }
    if (-not (Test-Path $resultFile)) {
        Write-Error "Timeout esperando o Unity gerar a build. Confirme que o Editor esta aberto neste projeto, ou use -SkipUnityBuild apos gerar manualmente."
        exit 1
    }
    $result = Get-Content $resultFile -Raw
    Write-Host "Resultado do build: $result"
    if ($result -notmatch "^Succeeded") {
        Write-Error "Build do Unity falhou: $result"
        exit 1
    }
} else {
    Step "Pulando build do Unity (-SkipUnityBuild) - usando build existente em $buildDir"
}

if (-not (Test-Path $buildDir)) {
    Write-Error "Pasta de build nao encontrada: $buildDir"
    exit 1
}

# 2. GitHub Releases (launcher proprio) ----------------------------------------------
if (-not $SkipGithub) {
    Step "Publicando no GitHub Releases (launcher proprio)"
    Push-Location (Join-Path $root "Launcher")
    try {
        & node "tools\publish.js" $buildDir --notes $Notes
        if ($LASTEXITCODE -ne 0) { throw "publish.js falhou (codigo $LASTEXITCODE)" }
    } finally {
        Pop-Location
    }
    Write-Host "Publicado no GitHub Releases." -ForegroundColor Green
} else {
    Step "Pulando GitHub Releases (-SkipGithub)"
}

# 3. itch.io ---------------------------------------------------------------------------
if (-not $SkipItch) {
    Step "Publicando no itch.io"
    & (Join-Path $PSScriptRoot "Publish-Itch.ps1") -BuildDir $buildDir -ItchUser $ItchUser -ItchGame $ItchGame -Channel $Channel
    if ($LASTEXITCODE -ne 0) { throw "Publish-Itch.ps1 falhou (codigo $LASTEXITCODE)" }
} else {
    Step "Pulando itch.io (-SkipItch)"
}

Step "Concluido"
Write-Host "Atualizacao publicada nos canais selecionados." -ForegroundColor Green
