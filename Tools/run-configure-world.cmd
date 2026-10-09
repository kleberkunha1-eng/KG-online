@echo off
"C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -batchmode -quit -projectPath "%~dp0.." -executeMethod TOP.EditorTools.PlayableWorldSetup.Configure -logFile "%~dp0..\Logs\ConfigurePlayableWorld.log"
exit /b %ERRORLEVEL%
