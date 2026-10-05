# Sobe a API + site e um tunel gratuito Cloudflare. O endereco muda a cada execucao.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$cf = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
$log = Join-Path $here "tunnel.log"
if (-not (Get-NetTCPConnection -LocalPort 3000 -State Listen -ErrorAction SilentlyContinue)) {
    Start-Process node -ArgumentList "server.js" -WorkingDirectory $here -WindowStyle Hidden
    Start-Sleep 5
}
Remove-Item $log -ErrorAction SilentlyContinue
Start-Process $cf -ArgumentList "tunnel --url http://localhost:3000" -WindowStyle Hidden -RedirectStandardError $log
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep 1
    $m = Select-String $log -Pattern "https://[a-z0-9-]+\.trycloudflare\.com" -ErrorAction SilentlyContinue | select -First 1
    if ($m) { $url = $m.Matches[0].Value; Write-Host "SITE ONLINE: $url"; Set-Content (Join-Path $here "public-url.txt") $url; break }
}
