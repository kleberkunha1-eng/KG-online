<#
.SYNOPSIS
    Corrige o bloqueio UDP do Unity Editor sem desligar o firewall nem liberar TCP.
.DESCRIPTION
    Execute em PowerShell com privilegios de administrador. Nao solicita elevacao
    automaticamente. Altera somente regras locais vinculadas ao executavel informado.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$EditorExe = "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe",
    [ValidateRange(1, 65535)]
    [int]$ServerPort = 7777
)

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Abra o PowerShell como administrador para corrigir a regra do Unity. Nenhuma regra foi alterada."
}
if (-not (Test-Path -LiteralPath $EditorExe -PathType Leaf)) {
    throw "Executavel do Unity nao encontrado: $EditorExe"
}
$EditorExe = (Resolve-Path -LiteralPath $EditorExe).Path
$addressFile = Join-Path $PSScriptRoot "..\server-address.txt"
$address = Get-Content -LiteralPath $addressFile | Where-Object { $_.Trim() -and -not $_.Trim().StartsWith("#") } | Select-Object -First 1
if ($address -notmatch '^[^:]+:(\d+)$') { throw "Endereco publico invalido em $addressFile" }
[uint16]$publicPort = 0
if (-not [uint16]::TryParse($Matches[1], [ref]$publicPort) -or $publicPort -eq 0) {
    throw "Porta publica invalida em $addressFile"
}
$ports = @($ServerPort, [int]$publicPort) | Sort-Object -Unique
$rules = @(Get-NetFirewallApplicationFilter -PolicyStore PersistentStore |
    Where-Object { $_.Program -eq $EditorExe } |
    Get-NetFirewallRule |
    Where-Object { $_.Enabled -eq "True" -and $_.Direction -eq "Inbound" -and $_.Action -eq "Block" })
$blocks = @()
foreach ($rule in $rules) {
    $filter = $rule | Get-NetFirewallPortFilter
    if ($filter.Protocol -eq "Any") {
        if ($rule.PolicyStoreSourceType -ne "Local") {
            throw "Regra gerenciada externamente: $($rule.Name). Contate o administrador; nenhuma regra foi alterada."
        }
        $blocks += $rule
    }
    elseif ($filter.Protocol -eq "UDP" -or $filter.Protocol -eq "17") {
        throw "Existe bloqueio UDP especifico ($($rule.Name)); requer revisao manual antes de alterar o firewall."
    }
}

$version = Split-Path (Split-Path (Split-Path $EditorExe -Parent) -Parent) -Leaf
$name = "TOP-Unity-$version-GameUdp"
$existing = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
if ($existing) {
    $application = $existing | Get-NetFirewallApplicationFilter
    if ($application.Program -ne $EditorExe) { throw "A regra $name pertence a outro executavel." }
}
if ($PSCmdlet.ShouldProcess($EditorExe, "Permitir somente UDP remoto nas portas $($ports -join ', ') e preservar bloqueio TCP")) {
    if ($existing) {
        $existing | Set-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -Profile Public,Private
        $existing | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol UDP -LocalPort Any -RemotePort $ports
    }
    else {
        New-NetFirewallRule -Name $name -DisplayName "TOP Unity $version - respostas UDP do jogo" `
            -Program $EditorExe -Direction Inbound -Action Allow -Protocol UDP `
            -LocalPort Any -RemotePort $ports -Profile Public,Private -Enabled True | Out-Null
    }
    foreach ($rule in $blocks) {
        $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP
        Write-Host "Bloqueio TCP preservado: $($rule.Name)"
    }
    $verified = Get-NetFirewallRule -Name $name
    $verifiedPorts = $verified | Get-NetFirewallPortFilter
    if ($verified.Enabled -ne "True" -or $verified.Action -ne "Allow" -or
        $verifiedPorts.Protocol -ne "UDP" -or
        @(Compare-Object @($verifiedPorts.RemotePort | ForEach-Object { [int]$_ }) $ports).Count -ne 0) {
        throw "A verificacao da regra UDP falhou. Revise $name antes de testar."
    }
    Write-Host "Regra UDP verificada. Firewall continua ligado. Execute Tools > PKO > Validate Multiplayer Transport no Unity."
}
