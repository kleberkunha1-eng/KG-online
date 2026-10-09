@echo off
setlocal
set "PATH=C:\TOP-Restoration\tools\node-v24.16.0-win-x64;C:\TOP-Restoration\tools\npm-global;%PATH%"
set "npm_config_prefix=C:\TOP-Restoration\tools\npm-global"
set "NODE_PATH=C:\TOP-Restoration\tools\npm-global\node_modules"
pushd "%~dp0"
call "%~dp0node_modules\.bin\wrangler.cmd" %*
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
