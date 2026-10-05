#Requires -Version 5.1
<#
.SYNOPSIS
    Verifica a assinatura Authenticode de um ou mais arquivos e falha (exit code != 0)
    se qualquer um deles nao estiver validamente assinado.

.DESCRIPTION
    Usado como portao de qualidade no final do pipeline de release: se um arquivo que
    deveria estar assinado nao estiver (ou a assinatura estiver corrompida/invalida), o
    release inteiro deve ser abortado - nunca publicar um arquivo "quase assinado".

    Combina duas fontes de verificacao:
      1. Get-AuthenticodeSignature (nativo do PowerShell) - mostra Status, SignerCertificate,
         Subject, Issuer e carimbo de tempo.
      2. signtool verify /pa /v (quando disponivel) - validacao adicional no mesmo padrao
         usado pelo proprio Windows ao abrir "Propriedades > Assinaturas Digitais".

.PARAMETER Files
    Caminho(s) do(s) arquivo(s) a verificar.

.PARAMETER AllowUnsigned
    Quando presente, arquivos sem assinatura sao reportados como aviso (nao erro). Use
    somente para builds de desenvolvimento (SIGNING_MODE=None) - nunca em release.
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Files,

    [switch]$AllowUnsigned
)

function Find-SignTool {
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

$signtool = Find-SignTool
$allOk = $true
$results = @()

foreach ($f in $Files) {
    if (-not (Test-Path $f)) {
        Write-Host "[ERROR] Arquivo nao encontrado: $f" -ForegroundColor Red
        $allOk = $false
        $results += [pscustomobject]@{ File = $f; Status = 'NotFound'; Publisher = $null }
        continue
    }
    $resolved = (Resolve-Path $f).Path
    $sig = Get-AuthenticodeSignature -FilePath $resolved
    $publisher = $null
    if ($sig.SignerCertificate) { $publisher = $sig.SignerCertificate.Subject }

    $isValid = $sig.Status -eq 'Valid'
    if (-not $isValid -and $AllowUnsigned -and $sig.Status -eq 'NotSigned') {
        Write-Host "[WARN] $(Split-Path $resolved -Leaf): nao assinado (permitido - modo desenvolvimento)." -ForegroundColor Yellow
        $results += [pscustomobject]@{ File = $resolved; Status = 'NotSigned (dev)'; Publisher = $publisher }
        continue
    }

    if ($isValid) {
        Write-Host "[OK] $(Split-Path $resolved -Leaf)"
        Write-Host "     Publisher : $publisher"
        Write-Host "     Issuer    : $($sig.SignerCertificate.Issuer)"
        if ($sig.TimeStamperCertificate) { Write-Host "     Timestamp : $($sig.TimeStamperCertificate.Subject)" }
        Write-Host "     Signature : Valid" -ForegroundColor Green
    } else {
        Write-Host "[ERROR] $(Split-Path $resolved -Leaf)" -ForegroundColor Red
        Write-Host "     Signature : $($sig.Status) - $($sig.StatusMessage)" -ForegroundColor Red
        $allOk = $false
    }
    $results += [pscustomobject]@{ File = $resolved; Status = $sig.Status; Publisher = $publisher }

    if ($signtool -and $isValid) {
        & $signtool verify /pa /v "$resolved" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] signtool verify falhou para $(Split-Path $resolved -Leaf) (codigo $LASTEXITCODE)." -ForegroundColor Red
            $allOk = $false
        }
    }
}

if (-not $allOk) {
    Write-Host ""
    Write-Host "ABORT RELEASE: uma ou mais assinaturas sao invalidas ou ausentes." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Todas as assinaturas verificadas com sucesso." -ForegroundColor Green
exit 0
