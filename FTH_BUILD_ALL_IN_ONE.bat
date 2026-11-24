@echo off
REM ==========================================================================
REM  FTH_BUILD_ALL_IN_ONE.bat  -  Field TAK Hub: Android + Builder Windows
REM                               kompilowane w GitHub Actions
REM
REM  Jeden skrypt: kontrola -> poprawki Buildera z paczki (jesli sa w _fth_patch)
REM  -> commit -> push (bez --force) -> GitHub Actions: aplikacja Android
REM  (android-hub-build.yml) i Builder Windows (windows-builder.yml) -> czekanie
REM  na wynik -> pobranie APK i Buildera .exe -> opcjonalnie adb install.
REM  Umiesc plik w katalogu glownym repozytorium (obok folderu apps).
REM  Plik workflow Androida jest wbudowany na koncu skryptu - gdy go brakuje
REM  w repozytorium, skrypt zapisuje go sam w .github\workflows.
REM
REM  Uzycie:
REM    FTH_BUILD_ALL_IN_ONE.bat                     debug, projekt wykrywany sam
REM    FTH_BUILD_ALL_IN_ONE.bat release             release podpisany w CI
REM    FTH_BUILD_ALL_IN_ONE.bat debug apps\mobile   stary projekt (test potoku)
REM    FTH_BUILD_ALL_IN_ONE.bat setup-debug-key     jednorazowo: staly klucz debug
REM    FTH_BUILD_ALL_IN_ONE.bat setup-release-key   jednorazowo: klucz release
REM    FTH_BUILD_ALL_IN_ONE.bat help
REM
REM  Wymagania: Git i GitHub CLI (gh). Gdy ich brakuje, skrypt proponuje instalacje
REM  przez winget, a gdy winget nie ma (np. komputer firmowy) - pobranie wersji
REM  przenosnej z oficjalnego wydania na GitHub do %LOCALAPPDATA%\FieldTakHub-tools
REM  (bez uprawnien administratora, z kontrola sumy SHA-256).
REM  Gdy folder nie jest repozytorium git, skrypt proponuje sklonowanie repozytorium
REM  i przeniesienie do niego plikow z tej paczki.
REM  Skrypt NIGDY nie wykonuje "git init" ani "git push --force".
REM  Zmienna FTH_NO_PAUSE=1 wylacza pauze na koncu (uruchamianie z konsoli).
REM ==========================================================================
cd /d "%~dp0"
setlocal EnableExtensions EnableDelayedExpansion
set "FTH_SELF=%~f0"
set "FTH_DIR=%~dp0"
if "%FTH_DIR:~-1%"=="\" set "FTH_DIR=%FTH_DIR:~0,-1%"
title Field TAK Hub - Android + Builder przez GitHub Actions

set "WORKFLOW=android-hub-build.yml"
set "WIN_WORKFLOW=windows-builder.yml"
set "DEFAULT_REPO_SLUG=QbcolPL/TEST"
set "WIN_CSPROJ=apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj"
set "DEFAULT_DIR=apps/android-hub"
set "LEGACY_DIR=apps/mobile"
set "GRADLE_VERSION=8.5"
set "BUILDS_ROOT=%USERPROFILE%\FieldTakHub-builds"
set "KEYS_ROOT=%USERPROFILE%\FieldTakHub-keys"
set "MAX_FILE_BYTES=99614720"
set "EXPECT_PUSH_RUN=0"
set "RUN_ID="
set "RUN_URL="
set "OUT_DIR="
set "PUSHED=0"
set "WIN_RUN_ID="
set "WIN_RUN_URL="
set "WIN_OUT_DIR="

set "MODE=%~1"
if not defined MODE set "MODE=debug"
if /i "%MODE%"=="help" goto :usage
if /i "%MODE%"=="-h" goto :usage
if /i "%MODE%"=="debug" goto :mode_ok
if /i "%MODE%"=="release" goto :mode_ok
if /i "%MODE%"=="setup-debug-key" goto :mode_ok
if /i "%MODE%"=="setup-release-key" goto :mode_ok
echo [BLAD] Nieznany tryb: %MODE%
set "USAGE_RC=1"
goto :usage

:mode_ok
set "TS="
for /f "delims=" %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd_HH-mm" 2^>nul') do set "TS=%%t"

echo ==========================================================================
echo  Field TAK Hub - Android + Builder w GitHub Actions   [tryb: %MODE%]
echo ==========================================================================
call :check_tools || goto :fail
call :find_repo || goto :fail
call :repo_slug || goto :fail
if exist "WSZYSTKO_W_JEDNYM_GITHUB.bat" echo [UWAGA] W repo jest WSZYSTKO_W_JEDNYM_GITHUB.bat ^(robi git push --force^) - usun go.
if exist "startactions.bat" echo [UWAGA] W repo jest startactions.bat ^(robi git push --force^) - usun go.

if /i "%MODE%"=="setup-debug-key" (
  call :setup_debug_key
  if errorlevel 1 goto :fail
  goto :done
)
if /i "%MODE%"=="setup-release-key" (
  call :setup_release_key
  if errorlevel 1 goto :fail
  goto :done
)

set "BUILD_TYPE=%MODE%"
call :detect_project "%~2" || goto :fail
call :apply_builder_fixes
call :ensure_workflow_file || goto :fail
call :stage_and_commit || goto :fail
call :push_branch || goto :fail
call :find_or_dispatch_run || goto :fail
call :find_builder_run
call :watch_run
set "RUN_RC=%errorlevel%"
call :download_artifacts
set "WIN_RC=0"
if defined WIN_RUN_ID call :watch_builder_run
if defined WIN_RUN_ID set "WIN_RC=!errorlevel!"
if defined WIN_RUN_ID call :download_builder_artifacts
if not "!WIN_RC!"=="0" call :builder_failed
if not "%RUN_RC%"=="0" goto :run_failed
call :offer_install || goto :fail
if not "!WIN_RC!"=="0" goto :fail
goto :done


REM ==========================================================================
REM  Podprogramy
REM ==========================================================================

:check_tools
where powershell >nul 2>&1
if errorlevel 1 (
  echo [BLAD] Nie znaleziono Windows PowerShell - jest wymagany do kontroli plikow.
  exit /b 1
)
set "FTH_TOOLS=%LOCALAPPDATA%\FieldTakHub-tools"
call :ensure_tool git.exe Git.Git "Git" "https://github.com/git-for-windows/git/releases/latest" git "%ProgramFiles%\Git\cmd" "%ProgramW6432%\Git\cmd" "%LOCALAPPDATA%\Programs\Git\cmd" "%LOCALAPPDATA%\FieldTakHub-tools\mingit\cmd" || exit /b 1
call :ensure_tool gh.exe GitHub.cli "GitHub CLI" "https://github.com/cli/cli/releases/latest" gh "%ProgramFiles%\GitHub CLI" "%ProgramW6432%\GitHub CLI" "%LOCALAPPDATA%\Programs\GitHub CLI" "%LOCALAPPDATA%\Programs\GitHub CLI\bin" "%LOCALAPPDATA%\FieldTakHub-tools\gh\bin" || exit /b 1
for /f "delims=" %%v in ('git --version') do echo [OK] %%v
for /f "tokens=1-3" %%a in ('gh --version ^| findstr /b /c:"gh version"') do echo [OK] %%a %%b %%c
gh auth status --hostname github.com >nul 2>&1
if not errorlevel 1 goto :ct_git_login
echo.
echo [INFO] Jednorazowe logowanie do GitHub: gh pokaze kod XXXX-XXXX i otworzy przegladarke.
echo        Wpisz kod na stronie GitHub i zatwierdz dostep.
echo        Na pytanie "Authenticate Git with your GitHub credentials?" mozesz odpowiedziec N -
echo        skrypt sam loguje git do GitHub, tylko w tym oknie.
gh auth login --hostname github.com --git-protocol https --web
if errorlevel 1 (
  echo [BLAD] Logowanie do GitHub nie powiodlo sie.
  exit /b 1
)
gh auth status --hostname github.com >nul 2>&1
if errorlevel 1 (
  echo [BLAD] gh nadal nie jest zalogowane. Sprawdz: gh auth status
  exit /b 1
)
echo [OK] Zalogowano do GitHub.
:ct_git_login
call :git_auth_env
exit /b 0


:git_auth_env
REM git loguje sie do GitHub tokenem konta zalogowanego w gh - tylko w tym oknie
REM (zmienne GIT_CONFIG_*), bez zapisywania czegokolwiek w konfiguracji git.
REM Dziala z przenosnym MinGit i nie koliduje z innym kontem GitHub zapisanym
REM w Menedzerze poswiadczen Windows (np. kontem firmowym).
set "FTH_BASIC="
set "FTH_B64="
set "FTH_GH_USER=x-access-token"
for /f "delims=" %%u in ('gh api user --jq .login 2^>nul') do set "FTH_GH_USER=%%u"
for /f "delims=" %%t in ('gh auth token --hostname github.com 2^>nul') do set "FTH_BASIC=!FTH_GH_USER!:%%t"
if not defined FTH_BASIC (
  echo [UWAGA] Nie udalo sie odczytac tokenu z gh - git uzyje wlasnego logowania.
  exit /b 0
)
for /f "delims=" %%b in ('powershell -NoProfile -Command "[Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($env:FTH_BASIC))"') do set "FTH_B64=%%b"
set "FTH_BASIC="
if not defined FTH_B64 exit /b 0
set "GIT_CONFIG_COUNT=1"
set "GIT_CONFIG_KEY_0=http.https://github.com/.extraheader"
set "GIT_CONFIG_VALUE_0=AUTHORIZATION: basic !FTH_B64!"
set "FTH_B64="
echo [OK] git loguje sie do GitHub kontem z gh ^(tylko w tym oknie^).
exit /b 0

:ensure_tool
REM Parametry: plik exe, id winget, nazwa, strona wydan, klucz wersji przenosnej (git/gh),
REM potem typowe katalogi instalacji.
set "ET_EXE=%~1"
set "ET_ID=%~2"
set "ET_NAME=%~3"
set "ET_URL=%~4"
set "ET_PORTABLE=%~5"
where %ET_EXE% >nul 2>&1
if not errorlevel 1 exit /b 0
REM Program moze byc zainstalowany, a to okno ma jeszcze stary PATH.
call :add_tool_path %*
if not errorlevel 1 exit /b 0
echo.
echo [INFO] Nie znaleziono programu: %ET_NAME%
where winget >nul 2>&1
if errorlevel 1 (
  echo [INFO] Na tym komputerze nie ma winget ^(Menedzera pakietow Windows^).
  goto :et_portable
)
choice /c TN /n /m "Zainstalowac %ET_NAME% teraz przez winget? [T/N] "
if errorlevel 2 goto :et_portable
echo [INFO] winget install --id %ET_ID% -e --source winget
echo        Windows moze poprosic o zgode administratora - potwierdz ja.
winget install --id %ET_ID% -e --source winget
call :add_tool_path %*
if not errorlevel 1 (
  echo [OK] Zainstalowano: %ET_NAME%
  exit /b 0
)
echo [UWAGA] winget nie zainstalowal programu %ET_NAME%.
:et_portable
echo [INFO] Moge pobrac przenosna wersje %ET_NAME% z oficjalnego wydania na GitHub
echo        do %FTH_TOOLS% - bez instalatora i bez uprawnien administratora.
choice /c TN /n /m "Pobrac teraz? [T/N] "
if errorlevel 2 goto :et_manual
call :portable_install %ET_PORTABLE%
if errorlevel 1 goto :et_manual
call :add_tool_path %*
if not errorlevel 1 (
  echo [OK] Gotowe: %ET_NAME% ^(wersja przenosna^)
  exit /b 0
)
:et_manual
echo [BLAD] Brak programu %ET_NAME% albo to okno jeszcze go nie widzi.
echo        Pobierz go recznie ze strony: %ET_URL%
if /i "%ET_PORTABLE%"=="gh" (
  echo        plik gh_WERSJA_windows_amd64.zip - rozpakuj go tak, aby powstal plik
  echo        !FTH_TOOLS!\gh\bin\gh.exe
)
if /i "%ET_PORTABLE%"=="git" (
  echo        plik MinGit-WERSJA-64-bit.zip - rozpakuj go tak, aby powstal plik
  echo        !FTH_TOOLS!\mingit\cmd\git.exe
)
echo        Potem uruchom skrypt ponownie.
exit /b 1

:portable_install
REM Wersja przenosna gh albo git (MinGit) z najnowszego oficjalnego wydania na GitHub.
REM Instalator PowerShell jest wbudowany na koncu tego pliku (sekcja __FTH_PORTABLE_PS1__).
set "FTH_PS1=%TEMP%\fth_portable_%RANDOM%%RANDOM%.ps1"
powershell -NoProfile -Command "$t = [IO.File]::ReadAllText($env:FTH_SELF); $a = [regex]::Match($t, '(?m)^:__FTH_PORTABLE_PS1__\r?$'); $b = [regex]::Match($t, '(?m)^:__FTH_WORKFLOW_YML__\r?$'); if ((-not $a.Success) -or (-not $b.Success) -or ($b.Index -le $a.Index)) { exit 1 }; $s = $t.Substring($a.Index + $a.Length, $b.Index - $a.Index - $a.Length); [IO.File]::WriteAllText($env:FTH_PS1, $s, (New-Object System.Text.UTF8Encoding $true)); exit 0"
if errorlevel 1 (
  echo [BLAD] Nie udalo sie przygotowac instalatora wersji przenosnej.
  del "%FTH_PS1%" >nul 2>&1
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%FTH_PS1%" -Tool %~1 -Root "%FTH_TOOLS%"
set "PI_RC=%errorlevel%"
del "%FTH_PS1%" >nul 2>&1
if not "%PI_RC%"=="0" exit /b 1
exit /b 0

:add_tool_path
REM Parametry jak w ensure_tool: plik exe, cztery pomijane, potem katalogi do sprawdzenia.
set "AT_EXE=%~1"
shift
shift
shift
shift
:at_next
shift
if "%~1"=="" exit /b 1
if not exist "%~1\%AT_EXE%" goto :at_next
set "PATH=%~1;!PATH!"
echo [OK] %AT_EXE%: %~1
exit /b 0

:find_repo
set "REPO_ROOT="
set "FTH_CLONED="
for /f "delims=" %%r in ('git rev-parse --show-toplevel 2^>nul') do set "REPO_ROOT=%%r"
if not defined REPO_ROOT if exist "!FTH_DIR!\_fth_patch\manifest.txt" call :clone_for_bundle
if not defined REPO_ROOT (
  echo [BLAD] Ten folder nie jest repozytorium git.
  echo        Sklonuj repozytorium i uruchom skrypt w klonie, np.: gh repo clone WLASCICIEL/REPO
  echo        Skrypt celowo nie wykonuje "git init" ani "git push --force".
  exit /b 1
)
set "REPO_ROOT=!REPO_ROOT:/=\!"
REM Archiwum rozpakowane do podfolderu repozytorium: pliki musza trafic do katalogu glownego.
if not defined FTH_CLONED if /i not "!FTH_DIR!"=="!REPO_ROOT!" if exist "!FTH_DIR!\_fth_patch\manifest.txt" (
  echo [UWAGA] Skrypt lezy w podfolderze repozytorium: !FTH_DIR!
  echo         Zawartosc archiwum musi byc w katalogu glownym: !REPO_ROOT!
  choice /c TN /n /m "Skopiowac zawartosc tego folderu do katalogu glownego teraz? [T/N] "
  if errorlevel 2 exit /b 1
  robocopy "!FTH_DIR!" "!REPO_ROOT!" /E /NFL /NDL /NJH /NJS /NP >nul
  if errorlevel 8 (
    echo [BLAD] Kopiowanie nie powiodlo sie.
    exit /b 1
  )
  echo [OK] Skopiowano. Teraz USUN folder !FTH_DIR!
  echo      ^(inaczej trafi do commita^) i uruchom !REPO_ROOT!\FTH_BUILD_ALL_IN_ONE.bat
  exit /b 1
)
cd /d "!REPO_ROOT!"
if errorlevel 1 exit /b 1
echo [OK] Repozytorium: !REPO_ROOT!
exit /b 0


:clone_for_bundle
REM Paczka rozpakowana poza repozytorium (np. w Pobranych albo w archiwum ZIP z GitHub).
REM Skrypt nie robi "git init" na kopii plikow: klonuje repozytorium i przenosi do klonu
REM tylko pliki z tej paczki, wiec starsza kopia nie nadpisze niczego na GitHub.
echo.
echo [INFO] Ten folder nie jest repozytorium git: !FTH_DIR!
echo        Moge sklonowac repozytorium z GitHub i przeniesc do niego pliki z tej paczki.
set "CL_SLUG="
set /p "CL_SLUG=Repozytorium WLASCICIEL/NAZWA (Enter = %DEFAULT_REPO_SLUG%, N = przerwij): "
if not defined CL_SLUG set "CL_SLUG=%DEFAULT_REPO_SLUG%"
if /i "!CL_SLUG!"=="N" exit /b 1
if "!CL_SLUG:/=!"=="!CL_SLUG!" (
  echo [BLAD] Podaj repozytorium w postaci WLASCICIEL/NAZWA.
  exit /b 1
)
REM Nazwa folderu klonu = ostatni czlon adresu (dziala tez dla pelnego adresu https://github.com/...).
set "CL_NAME=!CL_SLUG!"
:cl_name_next
if not "!CL_NAME:*/=!"=="!CL_NAME!" (
  set "CL_NAME=!CL_NAME:*/=!"
  goto :cl_name_next
)
if not defined CL_NAME (
  echo [BLAD] Niepoprawna nazwa repozytorium: !CL_SLUG!
  exit /b 1
)
set "CL_DIR=%USERPROFILE%\FieldTakHub-repo\!CL_NAME!"
if exist "!CL_DIR!\.git" (
  echo [INFO] Uzywam istniejacego klonu: !CL_DIR!
) else (
  if not exist "%USERPROFILE%\FieldTakHub-repo" mkdir "%USERPROFILE%\FieldTakHub-repo"
  gh repo clone "!CL_SLUG!" "!CL_DIR!"
  if errorlevel 1 (
    echo [BLAD] Nie udalo sie sklonowac !CL_SLUG!.
    exit /b 1
  )
)
echo [INFO] Przenosze pliki paczki do !CL_DIR! ...
copy /y "!FTH_DIR!\FTH_BUILD_ALL_IN_ONE.bat" "!CL_DIR!\FTH_BUILD_ALL_IN_ONE.bat" >nul
robocopy "!FTH_DIR!\_fth_patch" "!CL_DIR!\_fth_patch" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 exit /b 1
if exist "!FTH_DIR!\apps\android-hub" (
  robocopy "!FTH_DIR!\apps\android-hub" "!CL_DIR!\apps\android-hub" /E /NFL /NDL /NJH /NJS /NP >nul
  if errorlevel 8 exit /b 1
)
if exist "!FTH_DIR!\.github\workflows\android-hub-build.yml" (
  if not exist "!CL_DIR!\.github\workflows" mkdir "!CL_DIR!\.github\workflows"
  copy /y "!FTH_DIR!\.github\workflows\android-hub-build.yml" "!CL_DIR!\.github\workflows\android-hub-build.yml" >nul
)
if exist "!FTH_DIR!\docs\AUDYT_KODU_BUILDERA.md" (
  if not exist "!CL_DIR!\docs" mkdir "!CL_DIR!\docs"
  copy /y "!FTH_DIR!\docs\AUDYT_KODU_BUILDERA.md" "!CL_DIR!\docs\AUDYT_KODU_BUILDERA.md" >nul
)
set "REPO_ROOT=!CL_DIR!"
set "FTH_CLONED=1"
echo [OK] Pliki paczki sa w klonie. Nastepnym razem uruchamiaj skrypt z folderu:
echo      !CL_DIR!
exit /b 0


:repo_slug
set "REPO="
for /f "delims=" %%n in ('gh repo view --json nameWithOwner --jq .nameWithOwner 2^>nul') do set "REPO=%%n"
if not defined REPO (
  echo [BLAD] gh nie rozpoznaje repozytorium GitHub dla tego folderu ^(remote origin^).
  echo        Sprawdz: git remote -v   oraz   gh repo set-default
  exit /b 1
)
echo [OK] GitHub: !REPO!
exit /b 0


:detect_project
set "ANDROID_DIR=%~1"
if defined ANDROID_DIR goto :dp_check
if exist "%DEFAULT_DIR:/=\%\settings.gradle.kts" (
  set "ANDROID_DIR=%DEFAULT_DIR%"
) else if exist "%LEGACY_DIR:/=\%\settings.gradle.kts" (
  set "ANDROID_DIR=%LEGACY_DIR%"
  echo [UWAGA] Brak %DEFAULT_DIR% - buduje STARY projekt %LEGACY_DIR% ^(test potoku CI^).
) else (
  echo [BLAD] Nie znaleziono projektu Android ^(%DEFAULT_DIR% ani %LEGACY_DIR%^).
  exit /b 1
)
:dp_check
set "ANDROID_DIR=!ANDROID_DIR:\=/!"
if "!ANDROID_DIR:~-1!"=="/" set "ANDROID_DIR=!ANDROID_DIR:~0,-1!"
set "ANDROID_DIR_WIN=!ANDROID_DIR:/=\!"
if not exist "!ANDROID_DIR_WIN!\settings.gradle.kts" if not exist "!ANDROID_DIR_WIN!\settings.gradle" (
  echo [BLAD] Katalog !ANDROID_DIR! nie zawiera settings.gradle.kts.
  exit /b 1
)
echo [OK] Projekt Android: !ANDROID_DIR!   wariant: !BUILD_TYPE!
exit /b 0


:apply_builder_fixes
REM Poprawki Buildera z paczki (opis w _fth_patch\CZYTAJ_MNIE.txt) leza w _fth_patch.
REM Skrypt PowerShell naklada je tylko na pliki w wersji bazowej - wszystkie albo zadne.
set "FTH_PATCH_PS=!REPO_ROOT!\_fth_patch\apply-builder-fixes.ps1"
if not exist "!FTH_PATCH_PS!" exit /b 0
echo [INFO] Poprawki Buildera z paczki - sprawdzam pliki...
powershell -NoProfile -ExecutionPolicy Bypass -File "!FTH_PATCH_PS!" -RepoRoot "!REPO_ROOT!"
set "AF_RC=!errorlevel!"
if "!AF_RC!"=="0" exit /b 0
echo [UWAGA] Kontynuuje bez poprawek Buildera ^(kod: !AF_RC!^). Android i Builder zbuduja sie z obecnego kodu.
exit /b 0


:ensure_workflow_file
set "FTH_WF=!REPO_ROOT!\.github\workflows\%WORKFLOW%"
if exist "!FTH_WF!" (
  echo [OK] Workflow: .github\workflows\%WORKFLOW%
  exit /b 0
)
echo [INFO] Brak .github\workflows\%WORKFLOW% - zapisuje kopie wbudowana w ten skrypt.
if not exist "!REPO_ROOT!\.github\workflows" mkdir "!REPO_ROOT!\.github\workflows"
powershell -NoProfile -Command "$t = [IO.File]::ReadAllText($env:FTH_SELF); $m = [regex]::Match($t, '(?m)^:__FTH_WORKFLOW_YML__\r?$'); if (-not $m.Success) { exit 1 }; $crlf = [string][char]13 + [char]10; $lf = [string][char]10; $y = $t.Substring($m.Index + $m.Length).TrimStart([char]13, [char]10).Replace($crlf, $lf); [IO.File]::WriteAllText($env:FTH_WF, $y, (New-Object System.Text.UTF8Encoding $false)); exit 0"
if errorlevel 1 goto :ewf_error
if not exist "!FTH_WF!" goto :ewf_error
echo [OK] Utworzono .github\workflows\%WORKFLOW% - trafi do commita razem z innymi zmianami.
exit /b 0
:ewf_error
echo [BLAD] Nie udalo sie zapisac .github\workflows\%WORKFLOW%.
echo        Skopiuj plik android-hub-build.yml recznie do folderu .github\workflows w repozytorium.
exit /b 1


:stage_and_commit
call :ensure_git_identity || exit /b 1
git add -A 2> "%TEMP%\fth_add_err.txt"
if errorlevel 1 (
  type "%TEMP%\fth_add_err.txt"
  del "%TEMP%\fth_add_err.txt" >nul 2>&1
  exit /b 1
)
del "%TEMP%\fth_add_err.txt" >nul 2>&1
REM Lokalna paczka poprawek nie trafia do repozytorium (jest tez w .gitignore).
git reset -q -- _fth_patch >nul 2>&1
set "STAGED=%TEMP%\fth_staged_%RANDOM%%RANDOM%.txt"
set "STAGED_CHK=%STAGED%.chk"
git -c core.quotePath=false diff --cached --name-status > "%STAGED%"
git -c core.quotePath=false diff --cached --name-only --diff-filter=ACMRT > "%STAGED_CHK%"
set "COUNT=0"
for /f "usebackq delims=" %%f in ("%STAGED%") do set /a COUNT+=1
if "!COUNT!"=="0" (
  echo [INFO] Brak zmian do commita - buduje biezacy HEAD.
  del "%STAGED%" "%STAGED_CHK%" >nul 2>&1
  exit /b 0
)
REM --- pliki, ktore nigdy nie moga trafic do repozytorium ---
powershell -NoProfile -Command "$l = Get-Content -LiteralPath $env:STAGED_CHK -Encoding UTF8; $bad = $l | Where-Object { $_ -match '\.(jks|keystore|p12|pfx|pem|key|ftak|apk|aab|env)$' -or $_ -match '(^|/)(keystore\.properties|local\.properties|google-services\.json|enroll\.url|channel\.url)$' }; if ($bad) { $bad | ForEach-Object { '   ' + $_ }; exit 1 }; exit 0" > "%STAGED%.bad"
if errorlevel 1 (
  echo.
  echo [STOP] W commicie sa pliki, ktore nie moga trafic do repozytorium:
  type "%STAGED%.bad"
  echo        Dodaj je do .gitignore albo przenies poza repozytorium.
  git reset -q
  del "%STAGED%" "%STAGED_CHK%" "%STAGED%.bad" >nul 2>&1
  exit /b 1
)
del "%STAGED%.bad" >nul 2>&1
REM --- pliki wieksze niz limit GitHub (100 MB) ---
powershell -NoProfile -Command "$l = Get-Content -LiteralPath $env:STAGED_CHK -Encoding UTF8; $big = $l | Where-Object { (Test-Path -LiteralPath $_ -PathType Leaf) -and ((Get-Item -LiteralPath $_).Length -gt [long]$env:MAX_FILE_BYTES) }; if ($big) { $big | ForEach-Object { '   ' + $_ }; exit 1 }; exit 0" > "%STAGED%.big"
if errorlevel 1 (
  echo.
  echo [STOP] Pliki wieksze niz 95 MB - GitHub ich nie przyjmie:
  type "%STAGED%.big"
  git reset -q
  del "%STAGED%" "%STAGED_CHK%" "%STAGED%.big" >nul 2>&1
  exit /b 1
)
del "%STAGED%.big" >nul 2>&1
echo.
echo Pliki do commita: !COUNT!
powershell -NoProfile -Command "Get-Content -LiteralPath $env:STAGED -Encoding UTF8 | Select-Object -First 40 | ForEach-Object { '   ' + $_ }"
if !COUNT! GTR 40 echo    ... oraz kolejne pliki
echo.
choice /c TN /n /m "Zatwierdzic te zmiany i wyslac je na GitHub? [T/N] "
if errorlevel 2 (
  git reset -q
  del "%STAGED%" "%STAGED_CHK%" >nul 2>&1
  echo [INFO] Przerwano. Nic nie zostalo wyslane.
  exit /b 1
)
set "MSG="
set /p "MSG=Opis zmian bez polskich znakow (Enter = domyslny): "
if not defined MSG set "MSG=build: android !BUILD_TYPE! + builder windows !TS!"
set "MSGFILE=%TEMP%\fth_msg_%RANDOM%%RANDOM%.txt"
> "%MSGFILE%" echo(!MSG!
git commit -q -F "%MSGFILE%"
set "RC=!errorlevel!"
del "%MSGFILE%" "%STAGED%" "%STAGED_CHK%" >nul 2>&1
if not "!RC!"=="0" (
  echo [BLAD] git commit nie powiodl sie.
  exit /b 1
)
for /f "delims=" %%c in ('git rev-parse --short HEAD') do echo [OK] Commit %%c
exit /b 0


:ensure_git_identity
REM Autor commitow. Gdy git go nie zna, skrypt proponuje dane konta GitHub zalogowanego
REM w gh: login i prywatny adres noreply (ID+login@users.noreply.github.com).
REM Ustawia je tylko dla tego repozytorium - globalna konfiguracja git zostaje bez zmian.
set "GI_NAME="
set "GI_EMAIL="
set "GI_SET_NAME="
set "GI_SET_EMAIL="
for /f "delims=" %%n in ('git config user.name 2^>nul') do set "GI_NAME=%%n"
for /f "delims=" %%e in ('git config user.email 2^>nul') do set "GI_EMAIL=%%e"
if defined GI_NAME if defined GI_EMAIL exit /b 0
set "GH_LOGIN="
set "GH_ID="
for /f "delims=" %%a in ('gh api user --jq .login 2^>nul') do set "GH_LOGIN=%%a"
for /f "delims=" %%a in ('gh api user --jq .id 2^>nul') do set "GH_ID=%%a"
if not defined GH_LOGIN goto :gi_manual
if not defined GH_ID goto :gi_manual
if not defined GI_NAME (
  set "GI_NAME=!GH_LOGIN!"
  set "GI_SET_NAME=1"
)
if not defined GI_EMAIL (
  set "GI_EMAIL=!GH_ID!+!GH_LOGIN!@users.noreply.github.com"
  set "GI_SET_EMAIL=1"
)
echo.
echo [INFO] Git nie zna autora commitow na tym komputerze. Proponuje dane konta GitHub:
echo          user.name  = !GI_NAME!
echo          user.email = !GI_EMAIL!
echo        To prywatny adres GitHub - Twoj e-mail nie trafi do historii repozytorium.
echo        Ustawienie dotyczy tylko tego repozytorium.
choice /c TN /n /m "Ustawic? [T/N] "
if errorlevel 2 goto :gi_manual
if defined GI_SET_NAME (
  git config user.name "!GI_NAME!"
  if errorlevel 1 goto :gi_manual
)
if defined GI_SET_EMAIL (
  git config user.email "!GI_EMAIL!"
  if errorlevel 1 goto :gi_manual
)
echo [OK] Autor commitow: !GI_NAME!, !GI_EMAIL!
exit /b 0
:gi_manual
echo [BLAD] Git nie zna autora commitow. Ustaw go dla tego repozytorium:
echo          git config user.name  "Imie Nazwisko"
echo          git config user.email "ID+login@users.noreply.github.com"
echo        Swoj prywatny adres znajdziesz na GitHub: Settings - Emails.
exit /b 1


:push_branch
set "BRANCH="
for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%b"
if not defined BRANCH exit /b 1
if /i "!BRANCH!"=="HEAD" (
  echo [BLAD] Odlaczony HEAD - przelacz sie na galaz, np.: git switch main
  exit /b 1
)
echo [INFO] Galaz: !BRANCH!
git fetch -q origin "!BRANCH!" >nul 2>&1
set "REMOTE_SHA="
for /f "delims=" %%s in ('git rev-parse --verify --quiet "refs/remotes/origin/!BRANCH!" 2^>nul') do set "REMOTE_SHA=%%s"
if defined REMOTE_SHA (
  git merge-base --is-ancestor "!REMOTE_SHA!" HEAD
  if errorlevel 1 (
    echo [INFO] Na GitHub sa nowsze zmiany - wykonuje git pull --rebase --autostash
    git pull --rebase --autostash -q origin "!BRANCH!"
    if errorlevel 1 (
      echo [BLAD] Konflikt podczas rebase. Rozwiaz konflikty albo cofnij: git rebase --abort
      exit /b 1
    )
  )
)
set "SHA="
for /f "delims=" %%s in ('git rev-parse HEAD') do set "SHA=%%s"
if "!REMOTE_SHA!"=="!SHA!" (
  echo [INFO] Commit !SHA:~0,7! jest juz na GitHub - nic do wyslania.
  exit /b 0
)
git push -u origin "!BRANCH!"
if errorlevel 1 (
  echo [BLAD] git push odrzucony. Nie uzywaj --force - sprawdz uprawnienia i stan galezi.
  echo        Konto zalogowane w gh musi miec prawo zapisu do !REPO!. Sprawdz: gh auth status
  exit /b 1
)
echo [OK] Wyslano !SHA:~0,7! na origin/!BRANCH!
set "PUSHED=1"
REM Push uruchamia workflow sam tylko dla main, wariantu debug i zmian w DEFAULT_DIR.
if defined REMOTE_SHA if /i "!BRANCH!"=="main" if /i "!BUILD_TYPE!"=="debug" if /i "!ANDROID_DIR!"=="%DEFAULT_DIR%" (
  for /f "delims=" %%c in ('git diff --name-only "!REMOTE_SHA!" "!SHA!" -- "%DEFAULT_DIR%"') do set "EXPECT_PUSH_RUN=1"
)
exit /b 0


:find_or_dispatch_run
set "RUN_ID="
if "!EXPECT_PUSH_RUN!"=="1" (
  echo [INFO] Czekam, az push uruchomi workflow...
  for /l %%i in (1,1,12) do (
    if not defined RUN_ID (
      call :sleep 5
      for /f "delims=" %%r in ('gh run list -R "!REPO!" --workflow "%WORKFLOW%" --commit !SHA! --event push --limit 1 --json databaseId --jq ".[0].databaseId // empty" 2^>nul') do set "RUN_ID=%%r"
    )
  )
)
if defined RUN_ID goto :frun_found
set "PREV_ID="
for /f "delims=" %%r in ('gh run list -R "!REPO!" --workflow "%WORKFLOW%" --commit !SHA! --event workflow_dispatch --limit 1 --json databaseId --jq ".[0].databaseId // empty" 2^>nul') do set "PREV_ID=%%r"
echo [INFO] Uruchamiam workflow %WORKFLOW% ^(workflow_dispatch^)...
REM Nowo wyslany plik workflow GitHub widzi dopiero po kilku sekundach - stad ponawianie.
set "DISPATCH_LOG=%TEMP%\fth_dispatch_%RANDOM%%RANDOM%.txt"
set "DISPATCHED="
for /l %%i in (1,1,7) do (
  if not defined DISPATCHED (
    gh workflow run "%WORKFLOW%" -R "!REPO!" --ref "!BRANCH!" -f "project_dir=!ANDROID_DIR!" -f "build_type=!BUILD_TYPE!" -f "gradle_version=%GRADLE_VERSION%" > "%DISPATCH_LOG%" 2>&1
    if errorlevel 1 (
      if %%i LSS 7 (
        echo [INFO] GitHub jeszcze nie widzi workflow - ponawiam za 10 s ^(proba %%i z 7^)
        call :sleep 10
      )
    ) else (
      set "DISPATCHED=1"
    )
  )
)
if not defined DISPATCHED (
  echo [BLAD] Nie udalo sie uruchomic workflow. Odpowiedz GitHub:
  type "%DISPATCH_LOG%"
  del "%DISPATCH_LOG%" >nul 2>&1
  echo        Plik .github\workflows\%WORKFLOW% musi byc na galezi domyslnej repozytorium,
  echo        a GitHub Actions musi byc wlaczone w ustawieniach repozytorium.
  exit /b 1
)
del "%DISPATCH_LOG%" >nul 2>&1
for /l %%i in (1,1,24) do (
  if not defined RUN_ID (
    call :sleep 5
    for /f "delims=" %%r in ('gh run list -R "!REPO!" --workflow "%WORKFLOW%" --commit !SHA! --event workflow_dispatch --limit 1 --json databaseId --jq ".[0].databaseId // empty" 2^>nul') do (
      if not "%%r"=="!PREV_ID!" set "RUN_ID=%%r"
    )
  )
)
if not defined RUN_ID (
  echo [BLAD] Po 2 minutach nie widac przebiegu workflow. Sprawdz zakladke Actions w repozytorium.
  exit /b 1
)
:frun_found
set "RUN_URL="
for /f "delims=" %%u in ('gh run view !RUN_ID! -R "!REPO!" --json url --jq .url 2^>nul') do set "RUN_URL=%%u"
echo [OK] Przebieg !RUN_ID!: !RUN_URL!
exit /b 0


:watch_run
echo [INFO] Kompilacja w GitHub Actions - podglad na zywo:
gh run watch !RUN_ID! -R "!REPO!" --exit-status --interval 10
if errorlevel 1 exit /b 1
exit /b 0


:download_artifacts
set "OUT_DIR=%BUILDS_ROOT%\run-!RUN_ID!"
if exist "!OUT_DIR!" rmdir /s /q "!OUT_DIR!"
mkdir "!OUT_DIR!" >nul 2>&1
gh run download !RUN_ID! -R "!REPO!" -D "!OUT_DIR!" >nul 2>&1
if errorlevel 1 echo [INFO] Przebieg !RUN_ID! nie ma artefaktow do pobrania.
exit /b 0


:offer_install
set "APK="
for /r "%OUT_DIR%" %%a in (*.apk) do if not defined APK set "APK=%%a"
if not defined APK (
  echo [BLAD] W artefakcie nie ma pliku APK: !OUT_DIR!
  exit /b 1
)
echo.
echo [OK] APK: !APK!
where adb >nul 2>&1
if errorlevel 1 goto :oi_end
set "ADB_STATE="
for /f "delims=" %%d in ('adb get-state 2^>nul') do set "ADB_STATE=%%d"
if /i not "!ADB_STATE!"=="device" goto :oi_end
choice /c TN /n /m "Telefon podlaczony przez USB. Zainstalowac APK (adb install -r)? [T/N] "
if errorlevel 2 goto :oi_end
adb install -r "!APK!"
if errorlevel 1 (
  echo [UWAGA] adb install nie powiodl sie. Przy INSTALL_FAILED_UPDATE_INCOMPATIBLE
  echo         odinstaluj stara wersje ^(inny klucz podpisu^) i ustaw staly klucz: setup-debug-key
)
:oi_end
start "" explorer "!OUT_DIR!"
exit /b 0


:run_failed
echo.
echo [BLAD] Kompilacja nie powiodla sie: !RUN_URL!
if not defined OUT_DIR set "OUT_DIR=%BUILDS_ROOT%\run-!RUN_ID!"
if not exist "!OUT_DIR!" mkdir "!OUT_DIR!" >nul 2>&1
gh run view !RUN_ID! -R "!REPO!" --log-failed > "!OUT_DIR!\build-failed.log" 2>&1
echo        Log nieudanych krokow: !OUT_DIR!\build-failed.log
set "FTH_LOG=!OUT_DIR!\build-failed.log"
call :show_log_errors
goto :fail


:find_builder_run
REM Builder Windows: windows-builder.yml startuje sam po pushu na main/master/release/**.
REM Gdy przebiegu dla tego commita nie ma, skrypt uruchamia go recznie (workflow_dispatch).
set "WIN_RUN_ID="
if not exist "%WIN_CSPROJ%" exit /b 0
if not exist ".github\workflows\%WIN_WORKFLOW%" (
  echo [UWAGA] Brak .github\workflows\%WIN_WORKFLOW% - pomijam kompilacje Buildera Windows.
  exit /b 0
)
set "WIN_TRIES=2"
if "!PUSHED!"=="1" set "WIN_TRIES=12"
echo [INFO] Szukam przebiegu Buildera Windows dla !SHA:~0,7!...
for /l %%i in (1,1,%WIN_TRIES%) do (
  if not defined WIN_RUN_ID (
    call :sleep 5
    for /f "delims=" %%r in ('gh run list -R "!REPO!" --workflow "%WIN_WORKFLOW%" --commit !SHA! --limit 1 --json databaseId --jq ".[0].databaseId // empty" 2^>nul') do set "WIN_RUN_ID=%%r"
  )
)
if defined WIN_RUN_ID goto :fbr_found
echo [INFO] Uruchamiam %WIN_WORKFLOW% ^(workflow_dispatch^)...
set "WIN_DISPATCHED="
for /l %%i in (1,1,3) do (
  if not defined WIN_DISPATCHED (
    gh workflow run "%WIN_WORKFLOW%" -R "!REPO!" --ref "!BRANCH!" >nul 2>&1
    if errorlevel 1 (
      call :sleep 10
    ) else (
      set "WIN_DISPATCHED=1"
    )
  )
)
if not defined WIN_DISPATCHED (
  echo [UWAGA] Nie udalo sie uruchomic %WIN_WORKFLOW% - Builder Windows nie zostanie zbudowany.
  exit /b 0
)
for /l %%i in (1,1,24) do (
  if not defined WIN_RUN_ID (
    call :sleep 5
    for /f "delims=" %%r in ('gh run list -R "!REPO!" --workflow "%WIN_WORKFLOW%" --commit !SHA! --event workflow_dispatch --limit 1 --json databaseId --jq ".[0].databaseId // empty" 2^>nul') do set "WIN_RUN_ID=%%r"
  )
)
if not defined WIN_RUN_ID (
  echo [UWAGA] Nie widac przebiegu %WIN_WORKFLOW% - sprawdz zakladke Actions w repozytorium.
  exit /b 0
)
:fbr_found
set "WIN_RUN_URL="
for /f "delims=" %%u in ('gh run view !WIN_RUN_ID! -R "!REPO!" --json url --jq .url 2^>nul') do set "WIN_RUN_URL=%%u"
echo [OK] Builder Windows - przebieg !WIN_RUN_ID!: !WIN_RUN_URL!
exit /b 0


:watch_builder_run
echo.
echo [INFO] Kompilacja Buildera Windows ^(.NET, testy, publish^) - podglad na zywo:
gh run watch !WIN_RUN_ID! -R "!REPO!" --exit-status --interval 10
if errorlevel 1 exit /b 1
exit /b 0


:download_builder_artifacts
set "WIN_OUT_DIR=%BUILDS_ROOT%\builder-run-!WIN_RUN_ID!"
if exist "!WIN_OUT_DIR!" rmdir /s /q "!WIN_OUT_DIR!"
mkdir "!WIN_OUT_DIR!" >nul 2>&1
gh run download !WIN_RUN_ID! -R "!REPO!" -D "!WIN_OUT_DIR!" >nul 2>&1
if errorlevel 1 (
  echo [INFO] Przebieg Buildera !WIN_RUN_ID! nie ma artefaktow do pobrania.
  exit /b 0
)
set "WIN_EXE="
for /r "%WIN_OUT_DIR%" %%x in (FieldTakHub.Builder.exe) do if exist "%%x" if not defined WIN_EXE set "WIN_EXE=%%x"
if defined WIN_EXE (
  echo [OK] Builder Windows: !WIN_EXE!
) else (
  echo [OK] Artefakty Buildera: !WIN_OUT_DIR!
)
exit /b 0


:builder_failed
echo.
echo [BLAD] Kompilacja Buildera Windows nie powiodla sie: !WIN_RUN_URL!
if not defined WIN_OUT_DIR set "WIN_OUT_DIR=%BUILDS_ROOT%\builder-run-!WIN_RUN_ID!"
if not exist "!WIN_OUT_DIR!" mkdir "!WIN_OUT_DIR!" >nul 2>&1
gh run view !WIN_RUN_ID! -R "!REPO!" --log-failed > "!WIN_OUT_DIR!\build-failed.log" 2>&1
echo        Log nieudanych krokow: !WIN_OUT_DIR!\build-failed.log
set "FTH_LOG=!WIN_OUT_DIR!\build-failed.log"
call :show_log_errors
exit /b 0


:show_log_errors
REM Z logu nieudanego kroku pokazuje to, co trzeba poprawic: bledy kompilatora (Kotlin "e: plik:linia",
REM C# "error CS....", javac/aapt "error:") i blok "What went wrong" z Gradle. Bez prefiksow
REM "zadanie / krok / czas" dodawanych przez gh. Gdy nic nie pasuje - ostatnie 40 linii jak dawniej.
powershell -NoProfile -Command "$p = $env:FTH_LOG; if (-not (Test-Path -LiteralPath $p)) { exit 0 }; $lines = @(Get-Content -LiteralPath $p -Encoding UTF8); $hits = New-Object System.Collections.Generic.List[string]; for ($i = 0; $i -lt $lines.Count; $i++) { $l = $lines[$i] -replace '^[^\t]*\t[^\t]*\t\S+\s', ''; if ($l -match '^(##\[error\])?e: |error CS\d+|error MSB\d+|: error:|^ERROR:|Manifest merger failed|What went wrong|Execution failed for task|\sFAILED\s*$|There were failing tests') { $hits.Add($l); if ($l -match 'What went wrong') { for ($k = 1; ($k -le 4) -and (($i + $k) -lt $lines.Count); $k++) { $hits.Add(($lines[$i + $k] -replace '^[^\t]*\t[^\t]*\t\S+\s', '')) } } } }; if ($hits.Count -eq 0) { Write-Host '       Ostatnie linie:'; $lines | Select-Object -Last 40 | ForEach-Object { Write-Host $_ } } else { Write-Host '       Najwazniejsze bledy (pelny log w pliku powyzej):'; $hits | Select-Object -Unique | Select-Object -First 40 | ForEach-Object { Write-Host ('       ' + ($_ -replace 'file:///home/runner/work/[^/]+/[^/]+/', '')) } }"
exit /b 0


:setup_debug_key
call :find_keytool || exit /b 1
if not exist "%KEYS_ROOT%" mkdir "%KEYS_ROOT%"
set "KS=%KEYS_ROOT%\fth-debug.keystore"
if exist "%KS%" (
  echo [INFO] Uzywam istniejacego klucza debug: !KS!
) else (
  "!KEYTOOL!" -genkeypair -keystore "%KS%" -storetype PKCS12 -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10950 -dname "CN=Android Debug,O=Android,C=US"
  if errorlevel 1 exit /b 1
)
call :set_secret_from_file ANDROID_DEBUG_KEYSTORE_BASE64 "%KS%" || exit /b 1
echo [OK] Sekret ANDROID_DEBUG_KEYSTORE_BASE64 ustawiony w !REPO!.
echo      Kopia klucza: %KS%
echo      Ten sam plik mozesz skopiowac do %%USERPROFILE%%\.android\debug.keystore dla buildow lokalnych.
exit /b 0


:setup_release_key
call :find_keytool || exit /b 1
if not exist "%KEYS_ROOT%" mkdir "%KEYS_ROOT%"
set "KS=%KEYS_ROOT%\fth-release.p12"
set "ALIAS=fieldtakhub"
if exist "%KS%" (
  echo [INFO] Klucz release juz istnieje i nie zostanie nadpisany: !KS!
) else (
  echo [INFO] Tworze klucz release. keytool zapyta o haslo ^(min. 6 znakow^) - zapamietaj je.
  "!KEYTOOL!" -genkeypair -keystore "%KS%" -storetype PKCS12 -alias !ALIAS! -keyalg RSA -keysize 4096 -validity 10950 -dname "CN=Field TAK Hub,O=Field TAK Hub,C=PL"
  if errorlevel 1 exit /b 1
)
call :set_secret_from_file ANDROID_RELEASE_KEYSTORE_BASE64 "%KS%" || exit /b 1
gh secret set ANDROID_RELEASE_KEY_ALIAS -R "!REPO!" --body "!ALIAS!"
if errorlevel 1 exit /b 1
echo.
echo [INFO] Teraz wpisz TO SAMO haslo, ktore podales dla keytool:
gh secret set ANDROID_RELEASE_KEYSTORE_PASSWORD -R "!REPO!"
if errorlevel 1 exit /b 1
echo.
echo [OK] Sekrety release ustawione w !REPO!.
echo      ZROB KOPIE ZAPASOWA pliku: %KS%
echo      Bez niego nie wydasz aktualizacji aplikacji dla graczy. Nigdy nie dodawaj go do repozytorium.
exit /b 0


:set_secret_from_file
set "FTH_IN=%~2"
set "FTH_OUT=%TEMP%\fth_b64_%RANDOM%%RANDOM%.txt"
powershell -NoProfile -Command "[IO.File]::WriteAllText($env:FTH_OUT, [Convert]::ToBase64String([IO.File]::ReadAllBytes($env:FTH_IN)))"
if errorlevel 1 (
  del "%FTH_OUT%" >nul 2>&1
  exit /b 1
)
gh secret set %~1 -R "%REPO%" < "%FTH_OUT%"
set "RC=%errorlevel%"
del "%FTH_OUT%" >nul 2>&1
if not "%RC%"=="0" exit /b 1
exit /b 0


:find_keytool
set "KEYTOOL="
for /f "delims=" %%k in ('where keytool 2^>nul') do if not defined KEYTOOL set "KEYTOOL=%%k"
if not defined KEYTOOL if defined JAVA_HOME if exist "%JAVA_HOME%\bin\keytool.exe" set "KEYTOOL=%JAVA_HOME%\bin\keytool.exe"
if not defined KEYTOOL if exist "%ProgramFiles%\Android\Android Studio\jbr\bin\keytool.exe" set "KEYTOOL=%ProgramFiles%\Android\Android Studio\jbr\bin\keytool.exe"
if not defined KEYTOOL (
  echo [BLAD] Nie znaleziono keytool ^(JDK^). Zainstaluj JDK 17, np.:
  echo          winget install --id EclipseAdoptium.Temurin.17.JDK -e
  echo        albo Android Studio, i uruchom skrypt ponownie.
  exit /b 1
)
echo [OK] keytool: !KEYTOOL!
exit /b 0


:sleep
set /a "_N=%~1+1"
ping -n %_N% 127.0.0.1 >nul 2>&1
exit /b 0


:usage
echo.
echo  FTH_BUILD_ALL_IN_ONE.bat [tryb] [katalog_projektu]
echo.
echo    debug              ^(domyslnie^) commit + push + build w GitHub Actions: APK debug
echo                       oraz Builder Windows ^(.exe^); pobranie obu do %%USERPROFILE%%\FieldTakHub-builds
echo    release            jak wyzej, APK release podpisany kluczem z sekretow repozytorium
echo    setup-debug-key    jednorazowo: staly klucz debug, aby APK aktualizowal sie bez odinstalowania
echo    setup-release-key  jednorazowo: klucz release i sekrety ANDROID_RELEASE_*
echo    help               ta pomoc
echo.
echo  katalog_projektu: domyslnie %DEFAULT_DIR%, a gdy go nie ma - %LEGACY_DIR%
echo  Przyklad: FTH_BUILD_ALL_IN_ONE.bat debug apps\mobile
echo.
if not defined USAGE_RC set "USAGE_RC=0"
if not defined FTH_NO_PAUSE pause
endlocal & exit /b %USAGE_RC%


:done
echo.
echo [GOTOWE]
if not defined FTH_NO_PAUSE pause
endlocal
exit /b 0


:fail
echo.
echo [PRZERWANO] Szczegoly powyzej. Skrypt nie wykonal zadnego "push --force".
if not defined FTH_NO_PAUSE pause
endlocal
exit /b 1

REM ==========================================================================
REM  Ponizej: wbudowany instalator wersji przenosnej (PowerShell, czyta go
REM  portable_install) i kopia .github\workflows\android-hub-build.yml (czyta ja
REM  ensure_workflow_file). cmd nigdy nie wykonuje tych linii.
REM ==========================================================================
goto :eof
:__FTH_PORTABLE_PS1__
# Field TAK Hub - wersja przenosna GitHub CLI (gh) albo Git (MinGit) bez winget
# i bez uprawnien administratora. Zrodlo: najnowsze oficjalne wydanie na GitHub.
# Plik jest sprawdzany suma SHA-256 (pole digest z API GitHub albo plik sum
# kontrolnych wydania); bez zgodnej sumy nic nie jest instalowane.
param(
    [Parameter(Mandatory = $true)][ValidateSet('gh', 'git')][string]$Tool,
    [Parameter(Mandatory = $true)][string]$Root
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ua = 'FieldTakHub-build-script'

function Get-LatestTag([string]$Repo) {
    # Bez API GitHub (limit zapytan): /releases/latest przekierowuje na /releases/tag/<wersja>.
    $req = [Net.HttpWebRequest]::Create("https://github.com/$Repo/releases/latest")
    $req.AllowAutoRedirect = $false
    $req.UserAgent = $ua
    $resp = $req.GetResponse()
    try { $location = [string]$resp.Headers['Location'] } finally { $resp.Close() }
    if ($location -notmatch '/releases/tag/([^/?#]+)$') { throw "Nie udalo sie ustalic najnowszej wersji $Repo." }
    return $Matches[1]
}

function Get-ExpectedFromSums([string]$SumsUrl, [string]$FileName) {
    $tmp = Join-Path $env:TEMP ('fth_sums_' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        Invoke-WebRequest -Uri $SumsUrl -OutFile $tmp -UserAgent $ua -UseBasicParsing
        $text = [IO.File]::ReadAllText($tmp)
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
    $m = [regex]::Match($text, '(?im)^([0-9a-f]{64})\s+\*?' + [regex]::Escape($FileName) + '\s*$')
    if ($m.Success) { return $m.Groups[1].Value.ToLowerInvariant() }
    return $null
}

try {
    if ($Root -notmatch 'FieldTakHub-tools') { throw "Nieprawidlowy katalog docelowy: $Root" }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    # Firmowe proxy z logowaniem Windows: przekaz biezace poswiadczenia (inaczej blad 407).
    try { [Net.WebRequest]::DefaultWebProxy.Credentials = [Net.CredentialCache]::DefaultNetworkCredentials } catch { }
    $arm = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') -or ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64')
    if ($Tool -eq 'gh') {
        $repo = 'cli/cli'
        $dest = Join-Path $Root 'gh'
        $exeName = 'gh.exe'
        $pattern = if ($arm) { '^gh_[0-9.]+_windows_arm64\.zip$' } else { '^gh_[0-9.]+_windows_amd64\.zip$' }
    }
    else {
        $repo = 'git-for-windows/git'
        $dest = Join-Path $Root 'mingit'
        $exeName = 'git.exe'
        $pattern = if ($arm) { '^MinGit-[0-9.]+-arm64\.zip$' } else { '^MinGit-[0-9.]+-64-bit\.zip$' }
    }

    $name = $null
    $url = $null
    $expected = $null
    $tag = $null
    $size = 0
    Write-Host "[INFO] Szukam najnowszego wydania github.com/$repo ..."
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/latest" -UserAgent $ua -UseBasicParsing
        $tag = [string]$release.tag_name
        $asset = @($release.assets | Where-Object { $_.name -match $pattern }) | Select-Object -First 1
        if ($asset) {
            $name = [string]$asset.name
            $url = [string]$asset.browser_download_url
            $size = [long]$asset.size
            if ([string]$asset.digest -match '^sha256:([0-9a-fA-F]{64})$') { $expected = $Matches[1].ToLowerInvariant() }
            if (-not $expected) {
                $sums = @($release.assets | Where-Object { $_.name -match 'checksums\.txt$' }) | Select-Object -First 1
                if ($sums) { $expected = Get-ExpectedFromSums ([string]$sums.browser_download_url) $name }
            }
            if ((-not $expected) -and $release.body) {
                $m = [regex]::Match([string]$release.body, [regex]::Escape($name) + '\s*\|\s*([0-9a-fA-F]{64})')
                if ($m.Success) { $expected = $m.Groups[1].Value.ToLowerInvariant() }
            }
        }
    }
    catch {
        Write-Host "[INFO] API GitHub nie odpowiedzialo ($($_.Exception.Message)) - probuje bez API."
    }

    if ((-not $name) -and ($Tool -eq 'gh')) {
        $tag = Get-LatestTag $repo
        $ver = $tag.TrimStart('v')
        $archName = if ($arm) { 'arm64' } else { 'amd64' }
        $name = "gh_${ver}_windows_${archName}.zip"
        $url = "https://github.com/$repo/releases/download/$tag/$name"
        $expected = Get-ExpectedFromSums "https://github.com/$repo/releases/download/$tag/gh_${ver}_checksums.txt" $name
    }
    if (-not $name) { throw "Nie znaleziono pliku do pobrania w najnowszym wydaniu $repo." }
    if (-not $expected) { throw "Brak sumy kontrolnej SHA-256 dla $name - nie instaluje niesprawdzonego pliku." }

    $zip = Join-Path $env:TEMP ('fth_' + $name)
    $sizeText = ''
    if ($size -gt 0) { $sizeText = ' (' + [math]::Round($size / 1MB, 1) + ' MB)' }
    Write-Host "[INFO] Pobieram $name$sizeText ..."
    Invoke-WebRequest -Uri $url -OutFile $zip -UserAgent $ua -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
        throw 'Suma SHA-256 pobranego pliku sie nie zgadza - plik usuniety, nic nie zainstalowano.'
    }
    Write-Host "[OK] Suma SHA-256 zgodna z wydaniem $tag."

    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
    $exe = Get-ChildItem -LiteralPath $dest -Recurse -Filter $exeName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $exe) { throw "Po rozpakowaniu nie ma pliku $exeName w $dest." }
    Write-Host "[OK] Zainstalowano $tag w $dest"
    exit 0
}
catch {
    Write-Host "[BLAD] Wersja przenosna: $($_.Exception.Message)"
    exit 1
}
:__FTH_WORKFLOW_YML__
# Field TAK Hub - kompilacja aplikacji Android w GitHub Actions.
#
# Uruchamianie:
#   * FTH_BUILD_ALL_IN_ONE.bat (workflow_dispatch z wejsciami ponizej),
#   * push na main ze zmianami w apps/android-hub/**,
#   * pull request ze zmianami w apps/android-hub/**.
#
# Sekrety (opcjonalne, ustawia je FTH_BUILD_ALL_IN_ONE.bat):
#   ANDROID_DEBUG_KEYSTORE_BASE64     staly klucz debug -> aktualizacja APK bez odinstalowania
#   ANDROID_RELEASE_KEYSTORE_BASE64   magazyn kluczy release (PKCS12, Base64)
#   ANDROID_RELEASE_KEYSTORE_PASSWORD haslo magazynu
#   ANDROID_RELEASE_KEY_ALIAS         alias klucza
#   ANDROID_RELEASE_KEY_PASSWORD      (opcjonalnie) haslo klucza, domyslnie = haslo magazynu

name: Android Hub build

on:
  workflow_dispatch:
    inputs:
      project_dir:
        description: "Katalog projektu Android (z settings.gradle.kts), wzgledem repozytorium"
        required: true
        default: "apps/android-hub"
        type: string
      build_type:
        description: "Wariant"
        required: true
        default: "debug"
        type: choice
        options:
          - debug
          - release
      gradle_version:
        description: "Wersja Gradle tylko dla projektu bez gradlew (stary apps/mobile: 8.5)"
        required: false
        default: "8.5"
        type: string
      java_version:
        description: "Wersja JDK"
        required: false
        default: "17"
        type: string
      lint:
        description: "Uruchom Android Lint"
        required: false
        default: false
        type: boolean
  push:
    branches: [main]
    paths:
      - "apps/android-hub/**"
  pull_request:
    paths:
      - "apps/android-hub/**"

permissions:
  contents: read

concurrency:
  group: android-hub-${{ github.event_name }}-${{ github.ref }}-${{ inputs.build_type || 'debug' }}-${{ inputs.project_dir || 'apps/android-hub' }}
  cancel-in-progress: true

env:
  PROJECT_DIR: ${{ inputs.project_dir || 'apps/android-hub' }}
  BUILD_TYPE: ${{ inputs.build_type || 'debug' }}
  GRADLE_VERSION: ${{ inputs.gradle_version || '8.5' }}
  JAVA_VERSION: ${{ inputs.java_version || '17' }}
  RUN_LINT: ${{ inputs.lint && 'true' || 'false' }}

jobs:
  build:
    name: APK ${{ inputs.build_type || 'debug' }}
    runs-on: ubuntu-latest
    timeout-minutes: 45
    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Sprawdz projekt i wariant
        shell: bash
        run: |
          set -euo pipefail
          case "$BUILD_TYPE" in
            debug|release) ;;
            *) echo "::error::build_type musi byc debug albo release"; exit 1 ;;
          esac
          case "$PROJECT_DIR" in
            /*|*..*) echo "::error::Niedozwolona sciezka projektu: $PROJECT_DIR"; exit 1 ;;
          esac
          if [ ! -f "$PROJECT_DIR/settings.gradle.kts" ] && [ ! -f "$PROJECT_DIR/settings.gradle" ]; then
            echo "::error::Brak settings.gradle(.kts) w katalogu $PROJECT_DIR"
            exit 1
          fi
          if [ -f "$PROJECT_DIR/gradlew" ]; then
            echo "USE_WRAPPER=true" >> "$GITHUB_ENV"
          else
            echo "USE_WRAPPER=false" >> "$GITHUB_ENV"
            echo "::notice::Projekt bez Gradle Wrapper - uzywam Gradle $GRADLE_VERSION"
          fi
          echo "VARIANT=${BUILD_TYPE^}" >> "$GITHUB_ENV"

      - name: JDK ${{ env.JAVA_VERSION }}
        uses: actions/setup-java@v5
        with:
          distribution: temurin
          java-version: ${{ env.JAVA_VERSION }}

      - name: Gradle (wrapper projektu)
        if: env.USE_WRAPPER == 'true'
        uses: gradle/actions/setup-gradle@v6

      - name: Gradle ${{ env.GRADLE_VERSION }} (projekt bez wrappera)
        if: env.USE_WRAPPER != 'true'
        uses: gradle/actions/setup-gradle@v6
        with:
          gradle-version: ${{ env.GRADLE_VERSION }}

      - name: Staly klucz debug z sekretu
        if: env.BUILD_TYPE == 'debug'
        shell: bash
        env:
          DEBUG_KS_B64: ${{ secrets.ANDROID_DEBUG_KEYSTORE_BASE64 }}
        run: |
          set -euo pipefail
          if [ -z "${DEBUG_KS_B64:-}" ]; then
            echo "::warning::Brak sekretu ANDROID_DEBUG_KEYSTORE_BASE64 - kazdy build ma inny podpis i aktualizacja na telefonie wymaga odinstalowania. Uruchom raz: FTH_BUILD_ALL_IN_ONE.bat setup-debug-key"
            exit 0
          fi
          dirs=("$HOME/.android")
          if [ -n "${ANDROID_USER_HOME:-}" ]; then dirs+=("$ANDROID_USER_HOME"); fi
          if [ -n "${ANDROID_SDK_HOME:-}" ]; then dirs+=("$ANDROID_SDK_HOME/.android"); fi
          for d in "${dirs[@]}"; do
            mkdir -p "$d"
            printf '%s' "$DEBUG_KS_B64" | tr -d '\r\n ' | base64 -d > "$d/debug.keystore"
          done
          echo "Uzyto stalego klucza debug z sekretu."

      - name: Build ${{ env.BUILD_TYPE }} i testy jednostkowe
        working-directory: ${{ env.PROJECT_DIR }}
        shell: bash
        run: |
          set -euo pipefail
          # Bledy kompilatora Kotlin jako adnotacje przy plikach (widoczne na stronie przebiegu
          # i w "gh run view"), zamiast samego stack trace Gradle.
          cat > "$RUNNER_TEMP/fth-kotlin-matcher.json" <<'JSON'
          {"problemMatcher":[{"owner":"fth-kotlinc","pattern":[{"regexp":"^e: file://(.+?):(\\d+):(\\d+) (.+)$","file":1,"line":2,"column":3,"message":4}]}]}
          JSON
          echo "::add-matcher::$RUNNER_TEMP/fth-kotlin-matcher.json"
          if [ "$USE_WRAPPER" = "true" ]; then
            chmod +x ./gradlew
            GRADLE=./gradlew
          else
            GRADLE=gradle
          fi
          tasks=("assemble${VARIANT}" "test${VARIANT}UnitTest")
          if [ "$RUN_LINT" = "true" ]; then tasks+=("lint${VARIANT}"); fi
          LOG="$RUNNER_TEMP/gradle-build.log"
          rc=0
          "$GRADLE" --no-daemon --stacktrace "${tasks[@]}" 2>&1 | tee "$LOG" || rc=$?
          if [ "$rc" -ne 0 ]; then
            {
              echo "### Kompilacja nie powiodla sie"
              echo ""
              echo '```'
              grep -E '^e: ' "$LOG" | sed -E 's#^e: file://[^ ]*/(apps/)#e: \1#' | head -n 40 || true
              grep -A4 -E '^\* What went wrong' "$LOG" | head -n 30 || true
              echo '```'
            } >> "$GITHUB_STEP_SUMMARY"
          fi
          exit "$rc"

      - name: Podpis release (zipalign + apksigner)
        if: env.BUILD_TYPE == 'release'
        shell: bash
        env:
          KS_B64: ${{ secrets.ANDROID_RELEASE_KEYSTORE_BASE64 }}
          KS_PASS: ${{ secrets.ANDROID_RELEASE_KEYSTORE_PASSWORD }}
          KEY_ALIAS: ${{ secrets.ANDROID_RELEASE_KEY_ALIAS }}
          KEY_PASS: ${{ secrets.ANDROID_RELEASE_KEY_PASSWORD }}
        run: |
          set -euo pipefail
          if [ -z "${KS_B64:-}" ] || [ -z "${KS_PASS:-}" ] || [ -z "${KEY_ALIAS:-}" ]; then
            echo "::error::Brak sekretow release. Uruchom raz: FTH_BUILD_ALL_IN_ONE.bat setup-release-key"
            exit 1
          fi
          export KS_PASS
          export KEY_PASS="${KEY_PASS:-$KS_PASS}"
          BT="$(ls -d "$ANDROID_HOME"/build-tools/*/ | sort -V | tail -n 1)"
          KS="$RUNNER_TEMP/release.p12"
          printf '%s' "$KS_B64" | tr -d '\r\n ' | base64 -d > "$KS"
          OUT="$RUNNER_TEMP/signed"
          mkdir -p "$OUT"
          mapfile -t apks < <(find "$PROJECT_DIR" -path '*/build/outputs/apk/*' -path '*/release/*' -name '*.apk' | sort)
          if [ "${#apks[@]}" -eq 0 ]; then
            echo "::error::Nie znaleziono APK release"
            rm -f "$KS"
            exit 1
          fi
          for apk in "${apks[@]}"; do
            base="$(basename "$apk" .apk)"
            if "${BT}apksigner" verify "$apk" > /dev/null 2>&1; then
              echo "APK juz podpisany przez Gradle: $apk"
              cp "$apk" "$OUT/$base.apk"
            else
              "${BT}zipalign" -p -f 4 "$apk" "$RUNNER_TEMP/aligned.apk"
              "${BT}apksigner" sign --ks "$KS" --ks-pass env:KS_PASS \
                --ks-key-alias "$KEY_ALIAS" --key-pass env:KEY_PASS \
                --out "$OUT/${base%-unsigned}-signed.apk" "$RUNNER_TEMP/aligned.apk"
            fi
          done
          rm -f "$KS" "$RUNNER_TEMP/aligned.apk"

      - name: Zbierz APK, SHA-256 i certyfikat podpisu
        shell: bash
        run: |
          set -euo pipefail
          DIST="$RUNNER_TEMP/dist"
          mkdir -p "$DIST"
          if [ "$BUILD_TYPE" = "release" ]; then
            cp "$RUNNER_TEMP"/signed/*.apk "$DIST"/
          else
            mapfile -t apks < <(find "$PROJECT_DIR" -path '*/build/outputs/apk/*' -path '*/debug/*' -name '*.apk' | sort)
            if [ "${#apks[@]}" -eq 0 ]; then
              echo "::error::Nie znaleziono APK debug"
              exit 1
            fi
            cp "${apks[@]}" "$DIST"/
          fi
          BT="$(ls -d "$ANDROID_HOME"/build-tools/*/ | sort -V | tail -n 1)"
          cd "$DIST"
          sha256sum ./*.apk > SHA256SUMS.txt
          {
            echo "### APK ${BUILD_TYPE} z ${PROJECT_DIR}"
            echo ""
            echo '```'
            cat SHA256SUMS.txt
            for f in ./*.apk; do
              echo "--- $f"
              "${BT}apksigner" verify --print-certs "$f" | grep -E 'certificate (DN|SHA-256 digest)' || true
            done
            echo '```'
          } >> "$GITHUB_STEP_SUMMARY"

      - name: Artefakt APK
        uses: actions/upload-artifact@v7
        with:
          name: fth-android-${{ env.BUILD_TYPE }}-${{ github.run_number }}
          path: ${{ runner.temp }}/dist/
          if-no-files-found: error
          retention-days: 14

      - name: Raporty po bledzie
        if: failure()
        uses: actions/upload-artifact@v7
        with:
          name: fth-android-reports-${{ github.run_number }}
          path: |
            ${{ env.PROJECT_DIR }}/**/build/reports/
            ${{ env.PROJECT_DIR }}/**/build/test-results/
          if-no-files-found: ignore
          retention-days: 7
