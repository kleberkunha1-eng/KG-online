@echo off
cd /d "%~dp0"
if "%~1"=="" (echo Arraste a pasta da build do Unity para este arquivo, ou: publicar-update.bat "C:\caminho\Build" "notas" & pause & exit /b)
node tools\publish.js "%~1" --notes "%~2"
pause
