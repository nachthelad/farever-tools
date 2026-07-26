@echo off
chcp 65001 >nul
title Desinstalar Farever Translate
setlocal

if "%~1"=="" goto automatico
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Uninstall-FareverTranslate.ps1" -GameDirectory "%~1"
goto fin

:automatico
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Uninstall-FareverTranslate.ps1"

:fin
set "RESULTADO=%ERRORLEVEL%"
echo.
if not "%RESULTADO%"=="0" echo La desinstalación no se completó.
pause
exit /b %RESULTADO%
