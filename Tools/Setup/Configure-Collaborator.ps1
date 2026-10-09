[CmdletBinding()]
param(
    [string]$UnityExe,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$api = 'https://gamekg.pages.dev'
$serverHost = 'pgsql-henderson.tun.ply.gg'
$serverPort = 22538
$versionFile = Join-Path $project 'ProjectSettings\ProjectVersion.txt'

function Invoke-Git {
    param([string[]]$Arguments)
    & git -C $project @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git falhou (codigo $LASTEXITCODE). Corrija o erro acima e execute novamente."
    }
}

try {
    foreach ($directory in @('Assets', 'Packages', 'ProjectSettings')) {
        if (-not (Test-Path -LiteralPath (Join-Path $project $directory) -PathType Container)) {
            throw "Pasta $directory ausente. Copie o configurador para a raiz do projeto clonado completo."
        }
    }
    $versionText = Get-Content -LiteralPath $versionFile -Raw
    if ($versionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)') {
        throw 'Versao do Unity nao encontrada em ProjectVersion.txt.'
    }
    $version = $Matches[1]
    if ($version -ne '6000.4.4f1') {
        throw "Projeto exige $version; este configurador foi preparado para 6000.4.4f1."
    }

    Write-Host "Projeto: $project"
    Write-Host "Unity: $version | API: $api | Multiplayer: ${serverHost}:$serverPort"
    if ($CheckOnly) {
        Write-Host 'Verificacao estrutural concluida; nenhum arquivo, download ou processo foi alterado.'
        exit 0
    }

    $openEditors = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($project, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
    if ($openEditors) {
        throw 'Feche o Editor deste projeto, salvando o trabalho, e execute novamente.'
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git nao encontrado no PATH. Instale Git for Windows com Git LFS e execute novamente.'
    }
    Invoke-Git -Arguments @('rev-parse', '--show-toplevel')
    Invoke-Git -Arguments @('lfs', 'version')
    Invoke-Git -Arguments @('lfs', 'install', '--local')
    Invoke-Git -Arguments @('lfs', 'pull')

    if (-not $UnityExe) {
        $candidates = @(
            (Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Unity\Hub\Editor\$version\Editor\Unity.exe")
        )
        $UnityExe = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (-not $UnityExe) {
        Write-Warning "Unity $version nao encontrado nos caminhos padrao."
        Write-Host 'O Hub sera aberto no instalador da versao exata. Conclua a instalacao/login e execute este arquivo novamente.'
        Write-Host 'Se ja instalado em outra pasta, execute este script com -UnityExe "CAMINHO\Unity.exe".'
        Start-Process 'unityhub://6000.4.4f1/360f97ecca93'
        exit 2
    }
    if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) {
        throw "Executavel Unity nao encontrado: $UnityExe"
    }
    $installedVersion = (Get-Item -LiteralPath $UnityExe).VersionInfo.ProductVersion
    if ($installedVersion -notlike "$version*") {
        throw "Executavel incorreto ($installedVersion). Selecione o Unity $version."
    }
    if (Get-ChildItem -LiteralPath (Join-Path $project 'Tools') -Filter '*.request' -File) {
        throw 'Ha arquivos .request em Tools. Revise-os antes de abrir Unity para evitar operacoes automaticas.'
    }

    Write-Host 'Verificando a API publicada (somente leitura)...'
    $health = Invoke-RestMethod -Uri "$api/api/health" -TimeoutSec 15
    if ($health.status -ne 'API Online') {
        throw 'A API nao retornou a resposta esperada. Nenhuma configuracao sera substituida.'
    }
    Resolve-DnsName -Name $serverHost -ErrorAction Stop | Out-Null
    Write-Warning 'DNS/HTTP nao comprovam multiplayer UDP. O servidor compartilhado e o playit precisam estar ativos no PC anfitriao, nao neste PC.'
    Write-Host 'Se aparecer Login OK seguido de timeout KCP: verifique servidor UDP 7777 e tunel no anfitriao.'
    Write-Host 'Nao aumente o timeout nem configure 127.0.0.1 no PC do colega: esse endereco aponta para o proprio PC dele.'
    Write-Host 'Cliente e servidor precisam da mesma versao dos scripts/mensagens Mirror; atualize os arquivos compartilhados antes de jogar.'

    $trackedConfig = & git -C $project ls-files -- api.json
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel verificar api.json no Git.' }
    if ($trackedConfig) {
        throw 'api.json esta rastreado no Git. Revise-o antes de substituir a configuracao da equipe.'
    }
    & git -C $project check-ignore --quiet -- api.json
    if ($LASTEXITCODE -eq 1) {
        $exclude = & git -C $project rev-parse --git-path info/exclude
        if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel localizar exclusoes locais do Git.' }
        if (-not [IO.Path]::IsPathRooted($exclude)) { $exclude = Join-Path $project $exclude }
        [IO.File]::AppendAllText($exclude, "`n/api.json`n", (New-Object Text.UTF8Encoding($false)))
    } elseif ($LASTEXITCODE -ne 0) {
        throw 'Nao foi possivel verificar exclusoes locais do Git.'
    }
    $configuration = [ordered]@{
        apiUrl = $api
        gameServerHost = $serverHost
        gameServerPort = $serverPort
    } | ConvertTo-Json
    $configPath = Join-Path $project 'api.json'
    if (Test-Path -LiteralPath $configPath) {
        $previous = Get-Content -LiteralPath $configPath -Raw
        if ($previous.Trim() -ne $configuration.Trim()) {
            $backupDirectory = Join-Path $project 'UserSettings\CollaboratorSetup'
            New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
            $backup = Join-Path $backupDirectory ('api-' + [Guid]::NewGuid().ToString('N') + '.json')
            Copy-Item -LiteralPath $configPath -Destination $backup
            Write-Host "Configuracao anterior preservada: $backup"
        }
    }
    [IO.File]::WriteAllText($configPath, $configuration, (New-Object Text.UTF8Encoding($false)))
    Write-Host 'Configuracao salva. Nao foram instalados banco, API local, tunel ou regras de firewall.'
    Write-Host 'No Editor, abra Assets > Scenes > LoginScene e entre em Play. A primeira importacao pode demorar.'
    Write-Host 'A abertura direta do Editor pode nao cadastrar o projeto no Hub; use Add project from disk para inclui-lo na lista.'
    Start-Process -FilePath $UnityExe -ArgumentList @(
        '-projectPath', "`"$project`"",
        "--api=$api", "--game-server=$serverHost", "--game-server-port=$serverPort"
    )
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
}
