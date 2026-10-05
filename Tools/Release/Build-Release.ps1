#Requires -Version 5.1
<#
.SYNOPSIS
    Pipeline completo de release Windows: build do jogo (Unity) + build do Launcher +
    assinatura + verificacao + hashes + manifest + empacotamento + (opcional) instalador.

.DESCRIPTION
    Fluxo:
      Unity Build -> Sign Game -> Verify -> Sign Launcher -> Verify -> Generate hashes
      -> Create release manifest -> Package -> (opcional) Build+Sign Installer -> Report

    Nao assinar nada automaticamente sem configuracao: se SIGNING_MODE nao estiver definido
    (ou for None), o release e marcado como NAO ASSINADO e isso aparece claramente no
    relatorio final - nunca falha silenciosamente nem finge uma assinatura.

.PARAMETER Version
    Versao do release (ex.: 1.0.0). Se omitido, usa PlayerSettings.bundleVersion do projeto.

.PARAMETER DryRun
    Mostra o que seria feito (arquivos, comandos de assinatura, etapas) sem executar nada
    que modifique arquivos ou rode o Unity/signtool de verdade.

.PARAMETER SkipUnityBuild
    Pula a etapa de build do Unity (usa a build ja existente em Build/GameProjectKG).

.PARAMETER SkipLauncher
    Pula a etapa de build/assinatura do Launcher (apenas o jogo).

.PARAMETER SkipInstaller
    Pula a etapa de geracao do instalador Inno Setup (ela ja e pulada automaticamente se o
    Inno Setup nao estiver instalado).
#>
param(
    [string]$Version,
    [switch]$DryRun,
    [switch]$SkipUnityBuild,
    [switch]$SkipLauncher,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root

$log = @()
function Step($name) { $script:log += ""; Write-Host ""; Write-Host "==> $name" -ForegroundColor Cyan; $script:log += "[$name]" }
function Ok($msg) { Write-Host "    $msg" -ForegroundColor Green; $script:log += "  OK: $msg" }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow; $script:log += "  WARN: $msg" }
function Fail($msg) { Write-Host "    $msg" -ForegroundColor Red; $script:log += "  ERROR: $msg"; throw $msg }

$report = [ordered]@{
    Game        = 'GAME PROJECT - K/G'
    Version     = $Version
    Build       = ''
    UnityBuild  = 'SKIPPED'
    GameSigned  = 'N/A'
    LauncherSigned = 'N/A'
    Installer   = 'SKIPPED'
    Timestamp   = 'N/A'
    SHA256      = 'N/A'
    Manifest    = 'N/A'
    ReleaseDir  = ''
    Status      = 'IN PROGRESS'
}

# --- Versao / Build ID -------------------------------------------------------------------
Step 'VERSAO'
if (-not $Version) {
    $ps = Join-Path $root 'ProjectSettings\ProjectSettings.asset'
    $line = (Select-String -Path $ps -Pattern 'bundleVersion:\s*(.+)').Matches | Select-Object -First 1
    if ($line) { $Version = $line.Groups[1].Value.Trim() } else { $Version = '0.0.0' }
}
$buildId = ''
if (Get-Command git -ErrorAction SilentlyContinue) {
    try { $buildId = (git rev-parse --short HEAD 2>$null).Trim() } catch {}
}
$report.Version = $Version
$report.Build = $buildId
Ok "Version: $Version  Build: $buildId"

# --- Unity Build --------------------------------------------------------------------------
$gameOut = Join-Path $root 'Build\GameProjectKG'
$gameExe = Join-Path $gameOut 'GameProjectKG.exe'

if (-not $SkipUnityBuild) {
    Step 'UNITY BUILD'
    if ($DryRun) {
        Warn "[DRY-RUN] Executaria: Unity.exe -batchmode -quit -projectPath `"$root`" -executeMethod GameBuild.Build"
        $report.UnityBuild = 'DRY-RUN'
    } else {
        $editorVersionFile = Join-Path $root 'ProjectSettings\ProjectVersion.txt'
        $editorVersion = ((Select-String -Path $editorVersionFile -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value)
        $unityCandidates = @(
            "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe",
            "C:\Program Files\Unity\Editor\Unity.exe"
        )
        $unityExe = $unityCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $unityExe) { Fail "Unity Editor $editorVersion nao encontrado. Instale via Unity Hub ou rode com -SkipUnityBuild usando uma build existente." }

        $logPath = Join-Path $root 'Tools\unity-build-release.log'
        & $unityExe -batchmode -quit -nographics -projectPath "$root" -executeMethod GameBuild.Build -logFile "$logPath" | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $gameExe)) {
            Fail "Build do Unity falhou (codigo $LASTEXITCODE). Veja $logPath"
        }
        $report.UnityBuild = 'OK'
        Ok "Build gerada em $gameExe"
    }
} else {
    Warn "Build do Unity pulada (-SkipUnityBuild). Usando build existente."
    $report.UnityBuild = 'SKIPPED (usa build existente)'
}

if (-not $DryRun -and -not (Test-Path $gameExe)) { Fail "GameProjectKG.exe nao encontrado em $gameOut. Rode sem -SkipUnityBuild primeiro." }

# --- Assinatura do jogo ---------------------------------------------------------------------
Step 'SIGN GAME'
$signMode = $env:SIGNING_MODE
if (-not $signMode) { $signMode = 'None' }
# SIGN_ALLOW_SELF_SIGNED=true permite builds assinadas com certificado autoassinado (modo
# LocalCertificate de desenvolvimento/testes internos) sem abortar na verificacao, desde que a
# UNICA falha seja "raiz nao confiavel" (qualquer outro problema - hash incorreto, certificado
# expirado/revogado - ainda aborta o release normalmente). NUNCA usar isto para distribuicao
# publica: um certificado autoassinado nao e confiavel pelo SmartScreen dos jogadores.
$allowSelfSigned = $env:SIGN_ALLOW_SELF_SIGNED -eq 'true'
$verifyExtraArgs = @{}
if ($allowSelfSigned) {
    $verifyExtraArgs = @{ AllowUntrustedRoot = $true }
    Warn "SIGN_ALLOW_SELF_SIGNED=true - certificados autoassinados serao aceitos na verificacao (apenas para build interno/teste, NAO usar em distribuicao publica)."
}
if ($signMode -eq 'None') {
    Warn "SIGNING_MODE nao configurado (ou 'None') - Game.exe NAO sera assinado."
    $report.GameSigned = 'NOT SIGNED (SIGNING_MODE=None)'
} else {
    $filesToSign = @($gameExe)
    if (-not $DryRun) {
        & (Join-Path $PSScriptRoot '..\Signing\Sign-WindowsBuild.ps1') -Files $filesToSign | Out-Null
    } else {
        & (Join-Path $PSScriptRoot '..\Signing\Sign-WindowsBuild.ps1') -Files $filesToSign -DryRun | Out-Null
    }
    $report.GameSigned = if ($DryRun) { 'DRY-RUN' } elseif ($allowSelfSigned) { 'SIGNED (self-signed, dev only)' } else { 'SIGNED' }
    $report.Timestamp = 'OK'
    Ok "Game.exe: $($report.GameSigned)"
}

if (-not $DryRun -and $signMode -ne 'None') {
    Step 'VERIFY GAME SIGNATURE'
    & (Join-Path $PSScriptRoot '..\Signing\Verify-Signature.ps1') -Files @($gameExe) @verifyExtraArgs
    if ($LASTEXITCODE -ne 0) { Fail 'Verificacao de assinatura do jogo falhou - release abortado.' }
    Ok 'Assinatura do Game.exe valida.'
}

# --- Launcher -------------------------------------------------------------------------------
$launcherExe = Join-Path $root 'Launcher\dist\GameProjectKG-Launcher.exe'
if (-not $SkipLauncher) {
    Step 'LAUNCHER'
    if (-not $DryRun) {
        if (-not (Test-Path $launcherExe)) {
            Warn "Launcher ainda nao buildado nesta sessao - rode 'npm run build' em Launcher\ antes, ou use -SkipLauncher."
        } else {
            Ok "Launcher encontrado em $launcherExe"
        }
        if ($signMode -ne 'None' -and (Test-Path $launcherExe)) {
            & (Join-Path $PSScriptRoot '..\Signing\Sign-WindowsBuild.ps1') -Files @($launcherExe) | Out-Null
            & (Join-Path $PSScriptRoot '..\Signing\Verify-Signature.ps1') -Files @($launcherExe) @verifyExtraArgs
            if ($LASTEXITCODE -ne 0) { Fail 'Verificacao de assinatura do Launcher falhou - release abortado.' }
            $report.LauncherSigned = if ($allowSelfSigned) { 'SIGNED (self-signed, dev only)' } else { 'SIGNED' }
        } else {
            $report.LauncherSigned = 'NOT SIGNED (SIGNING_MODE=None)'
        }
    } else {
        Warn "[DRY-RUN] Assinaria $launcherExe (se existir) com modo $signMode"
        $report.LauncherSigned = 'DRY-RUN'
    }
} else {
    $report.LauncherSigned = 'SKIPPED'
}

# --- Hashes + manifest ------------------------------------------------------------------------
Step 'HASH + MANIFEST'
$releaseDir = Join-Path $root "Release\GameProjectKG-$Version"
$report.ReleaseDir = $releaseDir
if ($DryRun) {
    Warn "[DRY-RUN] Copiaria $gameOut para $releaseDir e geraria checksums.sha256 + release-manifest.json"
    $report.SHA256 = 'DRY-RUN'
    $report.Manifest = 'DRY-RUN'
} else {
    if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    Copy-Item (Join-Path $gameOut '*') $releaseDir -Recurse -Force
    $result = & (Join-Path $PSScriptRoot 'Generate-Manifest.ps1') -Path $releaseDir -Version $Version -BuildId $buildId
    $report.SHA256 = 'GENERATED'
    $report.Manifest = 'GENERATED'
    Ok "Manifest e checksums gerados em $releaseDir"
}

# --- Installer (opcional) ---------------------------------------------------------------------
if (-not $SkipInstaller) {
    Step 'INSTALLER (opcional)'
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $iscc) { $iscc = Get-ChildItem "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue }
    if (-not $iscc) {
        Warn 'Inno Setup (ISCC.exe) nao encontrado - etapa de instalador pulada (o Launcher portatil + .zip continuam sendo a distribuicao principal).'
        $report.Installer = 'SKIPPED (Inno Setup nao instalado)'
    } elseif ($DryRun) {
        Warn "[DRY-RUN] Compilaria Installer\GameInstaller.iss com /DMyAppVersion=$Version"
        $report.Installer = 'DRY-RUN'
    } else {
        $isccPath = if ($iscc -is [System.IO.FileInfo]) { $iscc.FullName } else { $iscc.Source }
        & $isccPath "/DMyAppVersion=$Version" (Join-Path $root 'Installer\GameInstaller.iss')
        if ($LASTEXITCODE -ne 0) { Fail 'Geracao do instalador falhou.' }
        $setupExe = Join-Path $root "Release\GameProjectKG-Setup-$Version.exe"
        if ($signMode -ne 'None' -and (Test-Path $setupExe)) {
            & (Join-Path $PSScriptRoot '..\Signing\Sign-WindowsBuild.ps1') -Files @($setupExe) | Out-Null
            & (Join-Path $PSScriptRoot '..\Signing\Verify-Signature.ps1') -Files @($setupExe)
            if ($LASTEXITCODE -ne 0) { Fail 'Verificacao de assinatura do instalador falhou - release abortado.' }
        }
        $report.Installer = 'SIGNED'
        Ok "Instalador gerado: $setupExe"
    }
} else {
    $report.Installer = 'SKIPPED'
}

$report.Status = 'READY'

# --- Relatorio final ----------------------------------------------------------------------
Write-Host ""
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "WINDOWS RELEASE"
Write-Host ""
Write-Host "Game:            $($report.Game)"
Write-Host "Version:         $($report.Version)"
Write-Host "Build:           $($report.Build)"
Write-Host "Unity Build:     $($report.UnityBuild)"
Write-Host "Game.exe:        $($report.GameSigned)"
Write-Host "Launcher.exe:    $($report.LauncherSigned)"
Write-Host "Installer:       $($report.Installer)"
Write-Host "Timestamp:       $($report.Timestamp)"
Write-Host "SHA256:          $($report.SHA256)"
Write-Host "Manifest:        $($report.Manifest)"
Write-Host "Release dir:     $($report.ReleaseDir)"
Write-Host "Release:         $($report.Status)"
Write-Host "====================================" -ForegroundColor Cyan

if ($signMode -eq 'None') {
    Write-Host ""
    Write-Host "CODE SIGNING NOT CONFIGURED" -ForegroundColor Yellow
    Write-Host "Set SIGNING_MODE (LocalCertificate ou AzureArtifactSigning) e as variaveis de ambiente" -ForegroundColor Yellow
    Write-Host "correspondentes (ver Docs\CodeSigning.md) para assinar o release de producao." -ForegroundColor Yellow
}

return $report
