@echo off
setlocal
if not exist "%~dp0Tools\Setup\Configure-Collaborator.ps1" (
    echo Falta o script Tools\Setup\Configure-Collaborator.ps1.
    echo Este configurador precisa dos dois arquivos; eles ainda nao foram enviados ao GitHub.
    echo Crie Tools\Setup dentro da pasta deste arquivo e copie Configure-Collaborator.ps1 para ela.
    echo Caminho esperado: "%~dp0Tools\Setup\Configure-Collaborator.ps1"
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Setup\Configure-Collaborator.ps1"
set "result=%ERRORLEVEL%"
if not "%result%"=="0" echo Configuracao nao concluida. Confira a mensagem acima.
pause
exit /b %result%
