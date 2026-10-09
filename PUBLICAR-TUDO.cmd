@echo off
rem Clique duas vezes para testar, compilar e publicar tudo (GitHub, Cloudflare/API, servidor 7777, itch.io).
rem Para apenas simular: PUBLICAR-TUDO.cmd -DryRun
title Publicar tudo - Tales of Pirates Unity
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Release\Publish-All.ps1" %*
echo.
if errorlevel 1 (echo PUBLICACAO FALHOU - leia as mensagens acima.) else (echo PUBLICACAO CONCLUIDA.)
pause
