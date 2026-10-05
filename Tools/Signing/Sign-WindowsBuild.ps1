#Requires -Version 5.1
<#
.SYNOPSIS
    Assina digitalmente (Authenticode) um ou mais arquivos .exe/.dll do Windows.

.DESCRIPTION
    Abstracao central de assinatura usada por todo o pipeline de release (build do jogo,
    build do Launcher e instalador). Suporta tres modos, controlados pela variavel de
    ambiente SIGNING_MODE:

      None                 - nao assina nada. Permitido apenas em builds de desenvolvimento.
      LocalCertificate     - assina com um certificado Authenticode local (.pfx/.p12).
      AzureArtifactSigning - assina usando Azure Trusted Signing (assinatura em nuvem).

    Nenhum segredo (senha de certificado, credenciais do Azure) deve ser passado por
    parametro de linha de comando nem gravado em arquivo dentro do repositorio - sempre
    via variaveis de ambiente, lidas apenas em memoria durante a execucao.

.PARAMETER Files
    Caminho(s) do(s) arquivo(s) a assinar (.exe ou .dll).

.PARAMETER DryRun
    Quando presente, apenas mostra o que seria feito (quais arquivos, qual modo, qual
    comando de assinatura) sem executar signtool nem modificar nenhum arquivo.

.NOTES
    Variaveis de ambiente usadas:
      SIGNING_MODE            None | LocalCertificate | AzureArtifactSigning
      SIGN_CERT_PATH          Caminho do certificado .pfx (modo LocalCertificate)
      SIGN_CERT_PASSWORD      Senha do certificado (modo LocalCertificate)
      SIGN_TIMESTAMP_URL      URL do servidor de timestamp RFC3161 (ambos os modos)
      AZURE_SIGNING_ACCOUNT   Nome da conta Trusted Signing (modo AzureArtifactSigning)
      AZURE_SIGNING_PROFILE   Nome do certificate profile (modo AzureArtifactSigning)
      AZURE_SIGNING_ENDPOINT  Endpoint regional da conta (modo AzureArtifactSigning)

    Autenticacao do Azure Trusted Signing usa a cadeia de credenciais padrao do Azure
    (Managed Identity / Azure CLI login / Service Principal via variaveis AZURE_* nativas
    do Azure SDK) - nunca um segredo hardcoded aqui.
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
    # Nao fixa uma versao do Windows SDK - procura a mais recente instalada.
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "$env:ProgramFiles\Windows Kits\10\bin"
    )
    $candidates = foreach ($root in $roots) {
        if (Test-Path $root) {
            Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' }
        }
    }
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) {
        $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($onPath) { $found = $onPath.Source }
    }
    return $found
}

function Find-AzureSignTool {
    $cmd = Get-Command AzureSignTool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $cmd = Get-Command AzureSignTool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$mode = $env:SIGNING_MODE
if (-not $mode) { $mode = 'None' }

Write-Host "[SIGN] Modo de assinatura: $mode"

$resolvedFiles = foreach ($f in $Files) {
    if (-not (Test-Path $f)) { throw "Arquivo nao encontrado: $f" }
    (Resolve-Path $f).Path
}

if ($mode -eq 'None') {
    Write-Host "[SIGN] SIGNING_MODE=None - nenhum arquivo sera assinado (permitido apenas para desenvolvimento)." -ForegroundColor Yellow
    foreach ($f in $resolvedFiles) { Write-Host "  - (nao assinado) $f" }
    return @{ Mode = 'None'; Signed = @() }
}

$timestampUrl = $env:SIGN_TIMESTAMP_URL
if (-not $timestampUrl) { $timestampUrl = 'http://timestamp.digicert.com' }

switch ($mode) {
    'LocalCertificate' {
        if (-not $env:SIGN_CERT_PATH) { throw "SIGNING_MODE=LocalCertificate requer SIGN_CERT_PATH (caminho do .pfx)." }
        if (-not (Test-Path $env:SIGN_CERT_PATH)) { throw "Certificado nao encontrado em: $($env:SIGN_CERT_PATH)" }
        if (-not $env:SIGN_CERT_PASSWORD) { throw "SIGNING_MODE=LocalCertificate requer SIGN_CERT_PASSWORD." }

        $signtool = Find-SignTool
        if (-not $signtool) { throw "signtool.exe nao encontrado. Instale o Windows SDK (Windows 10/11 SDK)." }
        Write-Host "[SIGN] signtool: $signtool"
        Write-Host "[SIGN] Certificado: $($env:SIGN_CERT_PATH)"
        Write-Host "[SIGN] Timestamp: $timestampUrl"

        $signed = @()
        foreach ($f in $resolvedFiles) {
            Write-Host "[SIGN] Assinando $f ..."
            if ($DryRun) {
                Write-Host "  [DRY-RUN] $signtool sign /f `"$($env:SIGN_CERT_PATH)`" /p **** /fd SHA256 /td SHA256 /tr `"$timestampUrl`" `"$f`""
            } else {
                & $signtool sign /f "$($env:SIGN_CERT_PATH)" /p "$($env:SIGN_CERT_PASSWORD)" /fd SHA256 /td SHA256 /tr "$timestampUrl" "$f"
                if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar $f (signtool saiu com codigo $LASTEXITCODE)." }
                $signed += $f
            }
        }
        return @{ Mode = $mode; Signed = $signed }
    }
    'AzureArtifactSigning' {
        foreach ($name in 'AZURE_SIGNING_ACCOUNT', 'AZURE_SIGNING_PROFILE', 'AZURE_SIGNING_ENDPOINT') {
            if (-not (Get-Item "env:$name" -ErrorAction SilentlyContinue)) { throw "SIGNING_MODE=AzureArtifactSigning requer $name." }
        }
        $ast = Find-AzureSignTool
        if (-not $ast -and -not $DryRun) { throw "AzureSignTool nao encontrado no PATH. Instale com: dotnet tool install --global AzureSignTool" }
        Write-Host "[SIGN] Azure Trusted Signing account: $($env:AZURE_SIGNING_ACCOUNT)"
        Write-Host "[SIGN] Certificate profile: $($env:AZURE_SIGNING_PROFILE)"
        Write-Host "[SIGN] Endpoint: $($env:AZURE_SIGNING_ENDPOINT)"
        Write-Host "[SIGN] Timestamp: $timestampUrl"
        Write-Host "[SIGN] Autenticacao via credential chain padrao do Azure (Managed Identity / az login / variaveis AZURE_CLIENT_ID etc.)."

        $signed = @()
        foreach ($f in $resolvedFiles) {
            Write-Host "[SIGN] Assinando $f via Azure Trusted Signing ..."
            if ($DryRun) {
                Write-Host "  [DRY-RUN] AzureSignTool sign -kvu `"$($env:AZURE_SIGNING_ENDPOINT)`" -kvc `"$($env:AZURE_SIGNING_PROFILE)`" -tr `"$timestampUrl`" -td SHA256 -fd SHA256 `"$f`""
            } else {
                & $ast sign -kvu "$($env:AZURE_SIGNING_ENDPOINT)" -kvc "$($env:AZURE_SIGNING_PROFILE)" -tr "$timestampUrl" -td SHA256 -fd SHA256 "$f"
                if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar $f via Azure Trusted Signing (codigo $LASTEXITCODE)." }
                $signed += $f
            }
        }
        return @{ Mode = $mode; Signed = $signed }
    }
    default { throw "SIGNING_MODE invalido: '$mode'. Use None, LocalCertificate ou AzureArtifactSigning." }
}
