@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM Field TAK Hub Builder 2.3.0 - LOCAL WINDOWS BUILD v3
REM Requires .NET 10 SDK (project target: net10.0-windows)
REM ============================================================

set "SCRIPT_DIR=%~dp0"
set "ROOT="
set "PROJECT=apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj"
set "OUT=artifacts\windows\FieldTakHub.Builder"

REM Find repository root: script in repo\tools\ OR repo root.
if exist "%SCRIPT_DIR%%PROJECT%" set "ROOT=%SCRIPT_DIR%"
if not defined ROOT if exist "%SCRIPT_DIR%..\%PROJECT%" for %%I in ("%SCRIPT_DIR%..") do set "ROOT=%%~fI\"

if not defined ROOT goto :NO_ROOT
cd /d "%ROOT%"

cls
echo ============================================================
echo FIELD TAK HUB BUILDER 2.3.0 - LOCAL WINDOWS BUILD
 echo ============================================================
echo.
echo Repozytorium:
echo   %ROOT%
echo.

echo [1/5] Sprawdzanie polecenia dotnet...
where dotnet >nul 2>&1
if errorlevel 1 goto :NO_DOTNET

echo OK: dotnet znaleziony.
echo.

echo [2/5] Sprawdzanie .NET SDK 10.x...
set "SDK10="
for /f "tokens=1" %%V in ('dotnet --list-sdks 2^>nul') do (
  echo %%V | findstr /r /b /c:"10\." >nul && set "SDK10=%%V"
)

if not defined SDK10 goto :NO_SDK10

echo OK: znaleziono .NET SDK !SDK10!
echo.

echo [3/5] Sprawdzanie projektu Buildera...
if not exist "%PROJECT%" (
  echo ERROR: Nie znaleziono:
  echo   %PROJECT%
  goto :FAIL
)
echo OK.
echo.

echo [4/5] Przywracanie zaleznosci NuGet...
dotnet restore "%PROJECT%"
if errorlevel 1 goto :RESTORE_FAIL

echo.
echo [5/5] Publikowanie Buildera win-x64 Self-Contained...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%" >nul 2>&1

dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=false ^
  -p:PublishTrimmed=false ^
  -p:PublishReadyToRun=false ^
  -o "%OUT%"
if errorlevel 1 goto :PUBLISH_FAIL

if not exist "%OUT%\FieldTakHub.Builder.exe" goto :EXE_FAIL

echo.
echo ============================================================
echo BUILD COMPLETE
 echo ============================================================
echo.
echo EXE:
echo   %ROOT%%OUT%\FieldTakHub.Builder.exe
echo.
echo FIELD NODE mapping:
echo   Field TAK Hub role : FIELD NODE
echo   Meshtastic role   : TAK
echo.
start "" explorer.exe "%ROOT%%OUT%"
echo Gotowe.
pause
exit /b 0

:NO_ROOT
cls
echo ============================================================
echo BUILD FAILED - NIE ZNALEZIONO REPOZYTORIUM
 echo ============================================================
echo.
echo Umiesc ten skrypt w:
echo   repo\tools\BUILD_WINDOWS_BUILDER_LOCAL_v3.cmd
 echo albo w katalogu glownym repozytorium.
echo.
goto :FAIL

:NO_DOTNET
cls
echo ============================================================
echo BUILD STOP - BRAK .NET
 echo ============================================================
echo.
echo Nie znaleziono polecenia dotnet w PATH.
echo.
echo Zainstaluj .NET 10 SDK x64, np.:
echo   winget install Microsoft.DotNet.SDK.10 --source winget
 echo.
echo Po instalacji zamknij to okno, otworz NOWE CMD i uruchom skrypt ponownie.
echo Oficjalna dokumentacja: https://learn.microsoft.com/dotnet/core/install/windows
 echo.
goto :FAIL

:NO_SDK10
cls
echo ============================================================
echo BUILD STOP - BRAK .NET SDK 10.x
 echo ============================================================
echo.
echo Polecenie dotnet istnieje, ale nie znaleziono SDK 10.x.
echo Ten Builder targetuje net10.0-windows.
echo.
echo Zainstaluj SDK:
echo   winget install Microsoft.DotNet.SDK.10 --source winget
 echo.
echo Sprawdz po instalacji:
echo   dotnet --list-sdks
 echo.
echo Powinienes zobaczyc wpis zaczynajacy sie od 10.
echo Po instalacji zamknij to okno, otworz NOWE CMD i uruchom skrypt ponownie.
echo.
where winget >nul 2>&1
if not errorlevel 1 (
  echo Winget jest dostepny. Mozesz wpisac ponizsze polecenie:
  echo   winget install Microsoft.DotNet.SDK.10 --source winget
  echo.
)
goto :FAIL

:RESTORE_FAIL
cls
echo ============================================================
echo BUILD FAILED - DOTNET RESTORE
 echo ============================================================
echo.
echo dotnet restore zakonczyl sie bledem.
echo Sprawdz komunikat powyzej i polaczenie z NuGet.
echo.
goto :FAIL

:PUBLISH_FAIL
cls
echo ============================================================
echo BUILD FAILED - DOTNET PUBLISH
 echo ============================================================
echo.
echo dotnet publish zakonczyl sie bledem.
echo Sprawdz komunikat powyzej.
echo.
goto :FAIL

:EXE_FAIL
cls
echo ============================================================
echo BUILD FAILED - BRAK EXE
 echo ============================================================
echo.
echo Publish zakonczyl sie, ale nie znaleziono:
echo   %ROOT%%OUT%\FieldTakHub.Builder.exe
 echo.
goto :FAIL

:FAIL
echo ============================================================
echo OKNO POZOSTANIE OTWARTE
 echo ============================================================
echo.
echo Kod bledu: %errorlevel%
echo Katalog roboczy: %CD%
echo.
pause
endlocal
exit /b 1
