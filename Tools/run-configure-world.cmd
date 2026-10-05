@echo off
"C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -batchmode -quit -projectPath "c:\Users\klebe\Tales of Pirates Unity" -executeMethod TOP.EditorTools.PlayableWorldSetup.Configure -logFile "c:\Users\klebe\Tales of Pirates Unity\Logs\ConfigurePlayableWorld.log"
exit /b %ERRORLEVEL%
