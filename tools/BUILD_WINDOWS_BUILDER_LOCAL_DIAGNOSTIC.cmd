@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM Field TAK Hub Builder 2.3.0 - LOCAL WINDOWS BUILD DIAGNOSTIC
REM Purpose: never erase compiler/publish errors; save full logs.
REM Requires .NET 10 SDK x64.
REM ============================================================

set "SCRIPT_DIR=%~dp0"
set "ROOT="
set "PROJECT=apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj"
set "OUT=artifacts\windows\FieldTakHub.Builder"
set "LOGDIR=artifacts\windows\logs"
set "RESTORE_LOG=%LOGDIR%\restore.log"
set "BUILD_LOG=%LOGDIR%\build.log"
set "PUBLISH_LOG=%LOGDIR%\publish.log"

if exist "%SCRIPT_DIR%%PROJECT%" set "ROOT=%SCRIPT_DIR%"
if not defined ROOT if exist "%SCRIPT_DIR%..\%PROJECT%" for %%I in ("%SCRIPT_DIR%..") do set "ROOT=%%~fI\"
if not defined ROOT goto :NO_ROOT

cd /d "%ROOT%"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

cls
echo ============================================================
echo FIELD TAK HUB BUILDER 2.3.0 - DIAGNOSTIC BUILD
echo ============================================================
echo.
echo Repozytorium:
echo   %ROOT%
echo.

echo [1/6] Sprawdzanie dotnet...
where dotnet >nul 2>&1
if errorlevel 1 goto :NO_DOTNET
dotnet --version
dotnet --list-sdks
echo.

echo [2/6] Sprawdzanie projektu...
if not exist "%PROJECT%" (
  echo ERROR: Nie znaleziono %PROJECT%
  goto :FAIL
)
echo OK.
echo.

echo [3/6] dotnet restore...
dotnet restore "%PROJECT%" > "%RESTORE_LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%RESTORE_LOG%"
if not "%RC%"=="0" goto :RESTORE_FAIL
echo OK.
echo.

echo [4/6] dotnet build Release (log bez czyszczenia ekranu)...
dotnet build "%PROJECT%" -c Release --no-restore > "%BUILD_LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%BUILD_LOG%"
if not "%RC%"=="0" goto :BUILD_FAIL
echo OK.
echo.

echo [5/6] dotnet publish win-x64...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%" >nul 2>&1

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -o "%OUT%" > "%PUBLISH_LOG%" 2>&1
set "RC=%ERRORLEVEL%"
type "%PUBLISH_LOG%"
if not "%RC%"=="0" goto :PUBLISH_FAIL

echo.
echo [6/6] Weryfikacja EXE...
if not exist "%OUT%\FieldTakHub.Builder.exe" goto :EXE_FAIL

echo.
echo ============================================================
echo BUILD COMPLETE
 echo ============================================================
echo EXE:
echo   %ROOT%%OUT%\FieldTakHub.Builder.exe
 echo Logi:
echo   %ROOT%%LOGDIR%\restore.log
 echo   %ROOT%%LOGDIR%\build.log
 echo   %ROOT%%LOGDIR%\publish.log
 echo.
start "" explorer.exe "%ROOT%%OUT%"
echo Gotowe.
pause
exit /b 0

:NO_ROOT
echo ERROR: Nie znaleziono repozytorium.
goto :FAIL

:NO_DOTNET
echo ERROR: Nie znaleziono dotnet w PATH.
goto :FAIL

:RESTORE_FAIL
echo.
echo ============================================================
echo RESTORE FAILED - PELNY LOG POWYZEJ
 echo ============================================================
goto :FAIL

:BUILD_FAIL
echo.
echo ============================================================
echo BUILD FAILED - PIERWSZY BLAD JEST POWYZEJ
 echo ============================================================
echo Log: %ROOT%%BUILD_LOG%
goto :FAIL

:PUBLISH_FAIL
echo.
echo ============================================================
echo PUBLISH FAILED - PELNY LOG JEST POWYZEJ
 echo ============================================================
echo Log: %ROOT%%PUBLISH_LOG%
goto :FAIL

:EXE_FAIL
echo ERROR: Publish zakonczyl sie bez FieldTakHub.Builder.exe
goto :FAIL

:FAIL
echo.
echo ============================================================
echo OKNO POZOSTANIE OTWARTE
 echo ============================================================
echo Kod bledu: %RC%
echo Katalog roboczy: %CD%
echo.
echo Prosze zachowac logi z artifacts\windows\logs\
pause
endlocal
exit /b 1
