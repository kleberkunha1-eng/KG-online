#Requires -Version 5.1
<#
.SYNOPSIS
    Gera checksums.sha256 e release-manifest.json para uma pasta de release.

.DESCRIPTION
    Percorre recursivamente a pasta informada, calcula SHA-256 de cada arquivo e grava:
      - checksums.sha256   (formato texto simples: "<hash>  <caminho relativo>")
      - release-manifest.json (version, build, createdAt, files[] com path/sha256/size)

.PARAMETER Path
    Pasta de release a ser processada (ex.: Release/GameProjectKG-1.0.0).

.PARAMETER Version
    Versao do release (ex.: 1.0.0). Se omitido, tenta usar PlayerSettings.bundleVersion
    gravado por quem chamou este script via -Version.

.PARAMETER BuildId
    Identificador do build. Se omitido, usa o commit curto do Git (quando disponivel).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$Version = '0.0.0',

    [string]$BuildId = ''
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Path)) { throw "Pasta nao encontrada: $Path" }
$root = (Resolve-Path $Path).Path

if (-not $BuildId) {
    $gitCmd = Get-Command git -ErrorAction SilentlyContinue
    if ($gitCmd) {
        try { $BuildId = (& git rev-parse --short HEAD 2>$null).Trim() } catch { $BuildId = '' }
    }
}

Write-Host "[HASH] Gerando SHA-256 para arquivos em $root ..."
$files = Get-ChildItem -Path $root -Recurse -File
$entries = @()
$checksumLines = @()

foreach ($f in $files) {
    $hash = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $rel = $f.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $entries += [ordered]@{ path = $rel; sha256 = $hash; size = $f.Length }
    $checksumLines += "$hash  $rel"
}

$checksumsPath = Join-Path $root 'checksums.sha256'
$checksumLines | Set-Content -Path $checksumsPath -Encoding ASCII
Write-Host "[HASH] checksums.sha256 gerado ($($entries.Count) arquivos)."

$manifest = [ordered]@{
    version     = $Version
    build       = $BuildId
    createdAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    files       = $entries
}
$manifestPath = Join-Path $root 'release-manifest.json'
($manifest | ConvertTo-Json -Depth 5) | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host "[HASH] release-manifest.json gerado (version=$Version build=$BuildId)."

return @{ Checksums = $checksumsPath; Manifest = $manifestPath; FileCount = $entries.Count }
