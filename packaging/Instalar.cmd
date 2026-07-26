@echo off
chcp 65001 >nul
title Instalar Farever Translate
setlocal

if "%~1"=="" goto automatico
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Install-FareverTranslate.ps1" -PayloadDirectory "%~dp0" -GameDirectory "%~1"
goto fin

:automatico
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Install-FareverTranslate.ps1" -PayloadDirectory "%~dp0"

:fin
set "RESULTADO=%ERRORLEVEL%"
echo.
if not "%RESULTADO%"=="0" echo La instalación no se completó.
pause
exit /b %RESULTADO%
