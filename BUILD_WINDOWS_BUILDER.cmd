@echo off
setlocal EnableExtensions

REM Field TAK Hub 2.3.0 - one-click Windows Builder launcher
REM This file can be started directly from the repository root.

set "ROOT=%~dp0"
set "BUILD=%ROOT%tools\BUILD_WINDOWS_BUILDER_LOCAL.cmd"

if not exist "%BUILD%" (
  echo ============================================================
  echo FIELD TAK HUB 2.3.0 - BUILDER
  echo ============================================================
  echo.
  echo ERROR: Nie znaleziono:
  echo   %BUILD%
  echo.
  echo Upewnij sie, ze uruchamiasz ten plik z rozpakowanego repozytorium.
  echo.
  pause
  exit /b 1
)

call "%BUILD%"
exit /b %errorlevel%
