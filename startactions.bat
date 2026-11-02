@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

echo ============================================================================
echo   AUTOMATYZACJA GITHUB ACTIONS — TWORZENIE PLIKOW I WYSYLKA
echo ============================================================================
echo.

:: 1. Automatyczne wykrycie folderu z projektem
set "TARGET="
if exist "apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj" set "TARGET=."
if exist "TEST-main\apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj" set "TARGET=TEST-main"
if exist "TEST-main-Gotowe\apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj" set "TARGET=TEST-main-Gotowe"

if "%TARGET%"=="" (
    echo [BLAD] Nie znaleziono katalogu z projektem FieldTakHub.Builder!
    echo Upewnij sie, ze skrypt jest w folderze glownym obok projektu.
    goto :error
)

echo [OK] Wykryto katalog projektu: %TARGET%
cd "%TARGET%"

:: 2. Sprawdzenie dostępności narzędzia Git
set "GIT_EXE=git"
if exist "%ProgramFiles%\Git\cmd\git.exe" set "GIT_EXE=%ProgramFiles%\Git\cmd\git.exe"
if exist "%LOCALAPPDATA%\Programs\Git\cmd\git.exe" set "GIT_EXE=%LOCALAPPDATA%\Programs\Git\cmd\git.exe"

"%GIT_EXE%" --version >nul 2>&1
if errorlevel 1 (
    echo [BLAD] Nie wykryto zainstalowanego programu Git w systemie!
    goto :error
)

:: 3. Tworzenie struktury katalogów dla GitHub Actions
echo.
echo [1/5] Tworzenie struktury .github/workflows/...
if not exist ".github\workflows" mkdir ".github\workflows"

:: 4. Generowanie pliku konfiguracyjnego workflow (windows-builder.yml)
echo [2/5] Generowanie pliku windows-builder.yml...
(
echo name: Windows Builder ^& Tests
echo.
echo on:
echo   push:
echo     branches: [ main, master ]
echo   pull_request:
echo     branches: [ main, master ]
echo.
echo jobs:
echo   build-and-test:
echo     runs-on: windows-latest
echo.
echo     steps:
echo     - name: Checkout repository
echo       uses: actions/checkout@v4
echo.
echo     - name: Setup .NET SDK
echo       uses: actions/setup-dotnet@v4
echo       with:
echo         dotnet-version: '9.0.x'
echo.
echo     - name: Restore dependencies
echo       run: dotnet restore apps/builder/FieldTakHub.Builder.sln
echo.
echo     - name: Build Solution
echo       run: dotnet build apps/builder/FieldTakHub.Builder.sln --configuration Release --no-restore
echo.
echo     - name: Run Unit Tests
echo       run: dotnet test apps/builder/FieldTakHub.Builder.Tests/FieldTakHub.Builder.Tests.csproj --configuration Release --no-restore --no-build
echo.
echo     - name: Publish Artifact
echo       run: dotnet publish apps/builder/FieldTakHub.Builder/FieldTakHub.Builder.csproj --configuration Release --output ./publish-artifact --no-restore
echo.
echo     - name: Upload Build Artifact
echo       uses: actions/upload-artifact@v4
echo       with:
echo         name: FieldTakHub-Builder-Release
echo         path: ./publish-artifact/
) > ".github\workflows\windows-builder.yml"

:: 5. Dodanie pliku kontrolnego / znacznika wyzwalającego
echo [3/5] Tworzenie pliku znacznika aktywności...
echo GitHub Actions trigger active > ".fth_build_trigger.txt"

:: 6. Operacje Git (Add, Commit, Push)
echo [4/5] Dodawanie plikow do repozytorium Git...
"%GIT_EXE%" add .github/workflows/windows-builder.yml
"%GIT_EXE%" add .fth_build_trigger.txt
"%GIT_EXE%" add -A

echo [5/5] Tworzenie commita i wysylanie na GitHub...
"%GIT_EXE%" commit -m "Add GitHub Actions workflow for automated Windows build and tests" --allow-empty
"%GIT_EXE%" push -u origin HEAD:main --force

if errorlevel 1 (
    echo.
    echo [UWAGA] Wystapil problem podczas operacji 'git push'. 
    echo Upewnij sie, ze masz uprawnienia do wypychania zmian lub zmien galaz docelowa (np. main na master).
    goto :error
)

echo.
echo ============================================================================
echo [SUKCES] Pliki zostaly utworzone i wyslane na GitHub Actions!
echo ============================================================================
echo Przejdz teraz do zakladki "Actions" w swoim repozytorium na GitHubie,
echo aby obserwowac przebieg kompilacji i pobrac gotowe artefakty (.exe/.dll).
echo.
pause
exit /b 0

:error
echo.
echo ============================================================================
echo [BŁĄD] Skrypt zostal przerwany. Sprawdz komunikaty powyzej.
echo ============================================================================
pause
exit /b 1