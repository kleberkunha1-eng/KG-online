$ErrorActionPreference = 'Stop'
$nodeDirectory = 'C:\TOP-Restoration\tools\node-v24.16.0-win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $nodeDirectory 'node.exe'))) {
    throw 'Node.js 24.16.0 local nao encontrado.'
}
$variables = @('PATH', 'HOST', 'PORT', 'DB_HOST', 'DB_PORT', 'CREATE_TEST_ACCOUNT')
$previous = @{}
foreach ($name in $variables) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:PATH = "$nodeDirectory;$env:PATH"
    $env:HOST = '127.0.0.1'
    $env:PORT = '3000'
    $env:DB_HOST = '127.0.0.1'
    $env:DB_PORT = '3307'
    $env:CREATE_TEST_ACCOUNT = '0'
    Push-Location $PSScriptRoot
    try {
        & (Join-Path $nodeDirectory 'node.exe') 'server.js'
        if ($LASTEXITCODE -ne 0) { throw "API local encerrou com codigo $LASTEXITCODE." }
    } finally {
        Pop-Location
    }
} finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}
