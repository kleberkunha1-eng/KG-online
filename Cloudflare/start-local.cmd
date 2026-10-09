@echo off
setlocal
call "%~dp0wrangler-local.cmd" pages dev "..\API\site" --ip 127.0.0.1 --port 3001 --persist-to "C:\TOP-Restoration\private\cloudflare-state"
exit /b %ERRORLEVEL%
