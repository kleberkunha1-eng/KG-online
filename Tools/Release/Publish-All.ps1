#Requires -Version 5.1
<#
.SYNOPSIS
    Publica tudo com um clique: testes -> build Unity (cliente + servidor) -> GitHub (commit/push)
    -> Cloudflare (migrations D1 + API/Pages) -> servidor dedicado 7777 (opcional) -> itch.io.

.DESCRIPTION
    Use o atalho PUBLICAR-TUDO.cmd na raiz do projeto. Para em qualquer falha ANTES de mexer
    em producao. Nunca imprime senhas/tokens e recusa commitar Secrets/.env/.dev.vars.

.PARAMETER DryRun        Apenas verifica (git, segredos, migrations pendentes, butler, Unity) sem alterar nada.
.PARAMETER SkipTests     Pula os testes da API.
.PARAMETER SkipBuild     Usa a ultima build de staging existente (Tools\gameplay-staging-build-results.txt).
.PARAMETER SkipGit       Nao faz commit/push.
.PARAMETER SkipCloudflare Nao aplica migrations nem publica a API.
.PARAMETER SkipItch      Nao envia ao itch.io.
.PARAMETER UpdateServer  Sim/Nao/Perguntar - substituir e reiniciar o servidor dedicado 7777 (desconecta jogadores).
.PARAMETER Message       Mensagem do commit (se vazio, pergunta; Enter usa uma mensagem com data).
#>
param(
    [switch]$DryRun,
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [switch]$SkipGit,
    [switch]$SkipCloudflare,
    [switch]$SkipItch,
    [ValidateSet('Perguntar', 'Sim', 'Nao')][string]$UpdateServer = 'Perguntar',
    [string]$Message
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $root

$git = 'C:\Program Files\Git\cmd\git.exe'
$node = 'C:\Program Files\nodejs\node.exe'
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe'
$butler = Join-Path $PSScriptRoot 'butler\butler.exe'
$wrangler = Join-Path $root 'Cloudflare\wrangler-local.cmd'
$itchTarget = 'kg-online/kg-online:windows'
$api = 'https://gamekg.pages.dev'
$serverDir = Join-Path $root 'Build\GameProjectKG.editorserver'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$version = Get-Date -Format 'yyyy.MM.dd-HHmm'
$logDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logFile = Join-Path $logDir "publish-all-$stamp.log"
Start-Transcript -Path $logFile | Out-Null

$summary = New-Object System.Collections.Generic.List[string]
function Step($t) { Write-Host ''; Write-Host "==> $t" -ForegroundColor Cyan }
function Ok($t) { Write-Host "    OK: $t" -ForegroundColor Green; $summary.Add("OK    $t") }
function Skip($t) { Write-Host "    PULADO: $t" -ForegroundColor Yellow; $summary.Add("PULADO $t") }
function Fail($t) { $summary.Add("FALHA $t"); throw $t }
function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments 2>&1 | ForEach-Object { "$_" } | Out-Host
    return $LASTEXITCODE
}

$exitCode = 0
try {
    Write-Host "Publicacao completa - Tales of Pirates Unity ($version)" -ForegroundColor White
    if ($DryRun) { Write-Host 'MODO SIMULACAO: nada sera alterado.' -ForegroundColor Yellow }

    # ---------- 0. Pre-verificacoes ----------
    Step 'Verificando ferramentas'
    foreach ($tool in @($git, $node, $unity, $butler, $wrangler)) {
        if (-not (Test-Path -LiteralPath $tool)) { Fail "Ferramenta ausente: $tool" }
    }
    Ok 'Git, Node, Unity, butler e wrangler encontrados'

    $unityOpen = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$root*" }
    if ($unityOpen -and -not $SkipBuild) {
        Fail 'O Unity esta aberto com este projeto. Feche o Editor e rode de novo (a build em lote precisa do projeto livre).'
    }

    if ($UpdateServer -eq 'Perguntar' -and -not $DryRun) {
        Write-Host ''
        Write-Host 'Atualizar e REINICIAR o servidor dedicado (porta 7777)?' -ForegroundColor Yellow
        Write-Host '  Recomendado quando cliente/servidor mudaram; desconecta quem estiver jogando por ~1 minuto.'
        $answer = Read-Host 'Digite S para sim ou N para nao'
        $UpdateServer = if ($answer -match '^[sSyY]') { 'Sim' } else { 'Nao' }
    }

    if (-not $SkipGit -and -not $DryRun -and [string]::IsNullOrWhiteSpace($Message)) {
        $Message = Read-Host 'Mensagem do commit (Enter = "Atualizacao <data>")'
    }
    if ([string]::IsNullOrWhiteSpace($Message)) { $Message = "Atualizacao $version" }

    # ---------- 1. Testes ----------
    Step 'Testes da API'
    if ($SkipTests) { Skip 'Testes da API' }
    else {
        $tests = Get-ChildItem (Join-Path $root 'Tools\tests') -Filter '*.test.cjs' | ForEach-Object FullName
        $maria = Get-Service | Where-Object { $_.Name -match 'maria|mysql' -and $_.Status -eq 'Running' }
        if ($maria) { $env:TOP_TEST_MARIADB = '1' } else { Write-Host '    MariaDB parado: testes de banco local serao ignorados.' -ForegroundColor Yellow }
        $code = Run $node (@('--test') + $tests)
        Remove-Item Env:TOP_TEST_MARIADB -ErrorAction SilentlyContinue
        if ($code -ne 0) { Fail "Testes da API falharam (codigo $code)" }
        Ok 'Testes da API'
    }

    # ---------- 2. Build Unity (cliente + servidor) ----------
    Step 'Build Unity (cliente + servidor dedicado)'
    $resultsFile = Join-Path $root 'Tools\gameplay-staging-build-results.txt'
    if ($SkipBuild -or $DryRun) { Skip 'Build Unity (usando a ultima build de staging)' }
    else {
        $before = if (Test-Path $resultsFile) { (Get-Item $resultsFile).LastWriteTime } else { [datetime]::MinValue }
        $unityLog = Join-Path $logDir "publish-unity-build-$stamp.log"
        Write-Host "    Compilando (5-12 minutos). Log: $unityLog"
        $p = Start-Process -FilePath $unity -PassThru -WindowStyle Minimized -ArgumentList @(
            '-batchmode', '-quit', '-projectPath', "`"$root`"",
            '-executeMethod', 'GameBuild.BuildGameplayStagingBatch', '-logFile', "`"$unityLog`"")
        $p.WaitForExit()
        if ($p.ExitCode -ne 0) { Fail "Build Unity falhou (codigo $($p.ExitCode)). Veja $unityLog" }
        if (-not (Test-Path $resultsFile) -or (Get-Item $resultsFile).LastWriteTime -le $before) {
            Fail "Build Unity nao gerou resultado novo. Veja $unityLog"
        }
    }
    if (-not (Test-Path $resultsFile)) { Fail 'Nenhuma build de staging encontrada.' }
    $results = Get-Content $resultsFile
    $clientLine = $results | Where-Object { $_ -match 'Staging ready: (\S+)' } | Select-Object -First 1
    $clientDir = if ($clientLine -match 'Staging ready: (\S+)') { Join-Path $root $Matches[1] }
    $serverLine = $results | Where-Object { $_ -match 'Dedicated Server ready: (\S+)' } | Select-Object -First 1
    $stagingServer = if ($serverLine -match 'Dedicated Server ready: (\S+)') { Join-Path $root $Matches[1] }
    if (-not ($clientLine -like 'Succeeded*' -and $clientLine -match '\| 0 errors') -or -not ($serverLine -like 'Succeeded*' -and $serverLine -match '\| 0 errors')) {
        Fail 'Ultima build de staging nao terminou com sucesso/0 erros.'
    }
    if (-not (Test-Path (Join-Path $clientDir 'GameProjectKG.exe')) -or -not (Test-Path (Join-Path $stagingServer 'GameProjectKG.exe'))) {
        Fail 'Executaveis da build de staging nao encontrados.'
    }
    Ok "Cliente: $clientDir"
    Ok "Servidor: $stagingServer"

    # ---------- 3. GitHub ----------
    Step 'GitHub (commit + push)'
    if ($SkipGit) { Skip 'GitHub' }
    else {
        $branch = (& $git rev-parse --abbrev-ref HEAD).Trim()
        & $git add -A
        $staged = @(& $git diff --cached --name-only)
        $secret = $staged | Where-Object { $_ -match '(^|/)(Secrets/|\.env($|\.)|\.dev\.vars|playit\.toml|.*\.(pfx|pem|key)$)' }
        if ($secret) {
            & $git reset -q
            Fail ("Arquivos sensiveis prontos para commit (cancelado): " + ($secret -join ', '))
        }
        $big = $staged | Where-Object { (Test-Path -LiteralPath $_) -and (Get-Item -LiteralPath $_).Length -gt 95MB }
        if ($big) {
            $lfs = $big | Where-Object { (& $git check-attr filter -- $_) -notmatch 'filter: lfs' }
            if ($lfs) { & $git reset -q; Fail ("Arquivos > 95MB sem Git LFS (limite do GitHub): " + ($lfs -join ', ')) }
        }
        if ($DryRun) {
            & $git reset -q
            Ok "Simulacao: $($staged.Count) arquivo(s) seriam enviados para $branch"
        }
        else {
            if ($staged.Count -gt 0) {
                $code = Run $git @('commit', '-q', '-m', $Message)
                if ($code -ne 0) { Fail 'git commit falhou' }
                Ok "Commit: $($staged.Count) arquivo(s) - $Message"
            } else { Write-Host '    Nada novo para commitar.' }
            $code = Run $git @('push', 'origin', $branch)
            if ($code -ne 0) { Fail 'git push falhou (verifique login do GitHub)' }
            Ok "Push para origin/$branch"
        }
    }

    # ---------- 4. Cloudflare (D1 + API/Pages) ----------
    Step 'Cloudflare (migrations D1 + API)'
    if ($SkipCloudflare) { Skip 'Cloudflare' }
    else {
        $code = Run $wrangler @('d1', 'migrations', 'list', 'gamekg-db', '--remote')
        if ($code -ne 0) { Fail 'Nao foi possivel consultar o D1 (login Cloudflare expirado? rode Cloudflare\wrangler-local.cmd login)' }
        if ($DryRun) { Ok 'Simulacao: migrations listadas acima seriam aplicadas e a API publicada' }
        else {
            $env:CI = 'true'
            $code = Run $wrangler @('d1', 'migrations', 'apply', 'gamekg-db', '--remote')
            Remove-Item Env:CI -ErrorAction SilentlyContinue
            if ($code -ne 0) { Fail 'Falha ao aplicar migrations D1 (API NAO publicada)' }
            Ok 'Migrations D1 aplicadas'
            $code = Run $wrangler @('pages', 'deploy', '..\API\site', '--project-name', 'gamekg', '--branch', 'main', '--commit-dirty=true')
            if ($code -ne 0) { Fail 'Falha ao publicar a API no Cloudflare Pages' }
            Start-Sleep -Seconds 10
            $health = Invoke-RestMethod -Uri "$api/api/health" -TimeoutSec 30
            if ($health.status -ne 'API Online') { Fail 'API publicada nao respondeu /api/health' }
            Ok "API publicada e online ($api)"
        }
    }

    # ---------- 5. Servidor dedicado 7777 ----------
    Step 'Servidor dedicado (UDP 7777)'
    if ($DryRun -or $UpdateServer -ne 'Sim') { Skip 'Servidor dedicado mantido como esta' }
    else {
        $running = Get-CimInstance Win32_Process -Filter "Name='GameProjectKG.exe'" |
            Where-Object { $_.ExecutablePath -eq (Join-Path $serverDir 'GameProjectKG.exe') }
        foreach ($proc in $running) {
            $wrapper = Get-CimInstance Win32_Process -Filter "ProcessId=$($proc.ParentProcessId)" -ErrorAction SilentlyContinue
            Stop-Process -Id $proc.ProcessId -Force
            if ($wrapper -and $wrapper.CommandLine -match 'Start-SharedServer') {
                Start-Sleep -Seconds 2
                Stop-Process -Id $wrapper.ProcessId -Force -ErrorAction SilentlyContinue
            }
        }
        for ($i = 0; $i -lt 30 -and (Get-NetUDPEndpoint -LocalPort 7777 -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
        if (Get-NetUDPEndpoint -LocalPort 7777 -ErrorAction SilentlyContinue) { Fail 'Porta 7777 continua ocupada.' }
        $previous = "$serverDir.previous.$stamp"
        if (Test-Path $serverDir) { Rename-Item -LiteralPath $serverDir -NewName (Split-Path $previous -Leaf) }
        $null = robocopy $stagingServer $serverDir /E /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { Fail 'Falha ao copiar o servidor novo' }
        $startServer = {
            Start-Process -FilePath 'powershell.exe' -WindowStyle Minimized -ArgumentList @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $PSScriptRoot 'Start-SharedServer.ps1')`"")
        }
        & $startServer
        $up = $false
        for ($i = 0; $i -lt 90 -and -not $up; $i++) { Start-Sleep -Seconds 1; $up = [bool](Get-NetUDPEndpoint -LocalPort 7777 -ErrorAction SilentlyContinue) }
        if (-not $up) {
            Write-Host '    Servidor novo nao abriu a 7777 - restaurando o anterior.' -ForegroundColor Red
            Get-CimInstance Win32_Process -Filter "Name='GameProjectKG.exe'" |
                Where-Object { $_.ExecutablePath -eq (Join-Path $serverDir 'GameProjectKG.exe') } |
                ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
            Start-Sleep -Seconds 3
            Rename-Item -LiteralPath $serverDir -NewName ((Split-Path $serverDir -Leaf) + ".failed.$stamp")
            Rename-Item -LiteralPath $previous -NewName (Split-Path $serverDir -Leaf)
            & $startServer
            Fail 'Servidor novo falhou; o anterior foi restaurado. Cliente NAO foi enviado ao itch.'
        }
        Ok "Servidor reiniciado na 7777 (anterior salvo em $previous)"
    }

    # ---------- 6. itch.io ----------
    Step 'itch.io (butler)'
    if ($SkipItch) { Skip 'itch.io' }
    elseif ($DryRun) {
        $code = Run $butler @('status', $itchTarget)
        if ($code -ne 0) { Fail 'butler sem login (rode Tools\Release\butler\butler.exe login)' }
        Ok "Simulacao: $clientDir seria enviado para $itchTarget"
    }
    else {
        $code = Run $butler @('push', $clientDir, $itchTarget, '--userversion', $version,
            '--ignore', '*_BurstDebugInformation_DoNotShip/**', '--ignore', '*_BackUpThisFolder_ButDontShipItWithYourGame/**',
            '--ignore', '*.pdb', '--ignore', 'UnityCrashHandler*')
        if ($code -ne 0) { Fail 'Falha ao enviar ao itch.io' }
        Ok "itch.io $itchTarget versao $version"
    }
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "ERRO: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Write-Host ''
    Write-Host '================ RESUMO ================' -ForegroundColor White
    $summary | ForEach-Object { Write-Host "  $_" }
    Write-Host "Log completo: $logFile"
    Stop-Transcript | Out-Null
}
exit $exitCode
