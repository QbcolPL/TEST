@echo off
REM ==========================================================================
REM  FTH_BUILD_ALL_IN_ONE.bat  -  Field TAK Hub: build Android przez GitHub Actions
REM
REM  Jeden skrypt: kontrola -> commit -> push (bez --force) -> GitHub Actions
REM  -> czekanie na wynik -> pobranie APK -> opcjonalnie adb install.
REM  Umiesc plik w katalogu glownym repozytorium (obok folderu apps).
REM  Plik workflow GitHub Actions jest wbudowany na koncu skryptu - gdy go brakuje
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
REM  Wymagania: Git for Windows i GitHub CLI. Gdy ich brakuje, skrypt proponuje
REM  instalacje przez winget i pracuje dalej w tym samym oknie.
REM  Skrypt NIGDY nie wykonuje "git init" ani "git push --force".
REM  Zmienna FTH_NO_PAUSE=1 wylacza pauze na koncu (uruchamianie z konsoli).
REM ==========================================================================
cd /d "%~dp0"
setlocal EnableExtensions EnableDelayedExpansion
set "FTH_SELF=%~f0"
title Field TAK Hub - Android build przez GitHub Actions

set "WORKFLOW=android-hub-build.yml"
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
echo  Field TAK Hub - build Android przez GitHub Actions   [tryb: %MODE%]
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
call :ensure_workflow_file || goto :fail
call :stage_and_commit || goto :fail
call :push_branch || goto :fail
call :find_or_dispatch_run || goto :fail
call :watch_run
set "RUN_RC=%errorlevel%"
call :download_artifacts
if not "%RUN_RC%"=="0" goto :run_failed
call :offer_install || goto :fail
goto :done


REM ==========================================================================
REM  Podprogramy
REM ==========================================================================

:check_tools
call :ensure_tool git.exe Git.Git "Git for Windows" "https://git-scm.com/download/win" "%ProgramFiles%\Git\cmd" "%ProgramW6432%\Git\cmd" "%LOCALAPPDATA%\Programs\Git\cmd" || exit /b 1
where powershell >nul 2>&1
if errorlevel 1 (
  echo [BLAD] Nie znaleziono Windows PowerShell - jest wymagany do kontroli plikow.
  exit /b 1
)
call :ensure_tool gh.exe GitHub.cli "GitHub CLI" "https://cli.github.com/" "%ProgramFiles%\GitHub CLI" "%ProgramW6432%\GitHub CLI" "%LOCALAPPDATA%\Programs\GitHub CLI" || exit /b 1
for /f "delims=" %%v in ('git --version') do echo [OK] %%v
for /f "tokens=1-3" %%a in ('gh --version ^| findstr /b /c:"gh version"') do echo [OK] %%a %%b %%c
gh auth status --hostname github.com >nul 2>&1
if not errorlevel 1 exit /b 0
echo.
echo [INFO] Jednorazowe logowanie do GitHub: gh pokaze kod XXXX-XXXX i otworzy przegladarke.
echo        Wpisz kod na stronie GitHub i zatwierdz dostep.
echo        Na pytanie "Authenticate Git with your GitHub credentials?" odpowiedz Y.
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
exit /b 0

:ensure_tool
REM Parametry: plik exe, id winget, nazwa, strona WWW, potem typowe katalogi instalacji.
set "ET_EXE=%~1"
set "ET_ID=%~2"
set "ET_NAME=%~3"
set "ET_URL=%~4"
where %ET_EXE% >nul 2>&1
if not errorlevel 1 exit /b 0
REM Program moze byc zainstalowany, a to okno ma jeszcze stary PATH.
call :add_tool_path %*
if not errorlevel 1 exit /b 0
echo.
echo [INFO] Nie znaleziono programu: %ET_NAME%
where winget >nul 2>&1
if errorlevel 1 goto :et_manual
choice /c TN /n /m "Zainstalowac %ET_NAME% teraz przez winget? [T/N] "
if errorlevel 2 goto :et_manual
echo [INFO] winget install --id %ET_ID% -e --source winget
echo        Windows moze poprosic o zgode administratora - potwierdz ja.
winget install --id %ET_ID% -e --source winget
call :add_tool_path %*
if not errorlevel 1 (
  echo [OK] Zainstalowano: %ET_NAME%
  exit /b 0
)
:et_manual
echo [BLAD] Brak programu %ET_NAME% albo to okno jeszcze go nie widzi.
echo        Zainstaluj go poleceniem:  winget install --id %ET_ID% -e
echo        albo ze strony:            %ET_URL%
echo        Potem zamknij to okno i uruchom skrypt ponownie.
exit /b 1

:add_tool_path
REM Parametry jak w ensure_tool: plik exe, trzy pomijane, potem katalogi do sprawdzenia.
set "AT_EXE=%~1"
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
for /f "delims=" %%r in ('git rev-parse --show-toplevel 2^>nul') do set "REPO_ROOT=%%r"
if not defined REPO_ROOT (
  echo [BLAD] Ten folder nie jest repozytorium git.
  echo        Sklonuj repozytorium i uruchom skrypt w klonie, np.: gh repo clone WLASCICIEL/REPO
  echo        Skrypt celowo nie wykonuje "git init" ani "git push --force".
  exit /b 1
)
set "REPO_ROOT=!REPO_ROOT:/=\!"
cd /d "!REPO_ROOT!"
if errorlevel 1 exit /b 1
echo [OK] Repozytorium: !REPO_ROOT!
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
git config user.email >nul 2>&1
if errorlevel 1 (
  echo [BLAD] Brak tozsamosci git. Ustaw ja raz:
  echo          git config --global user.name  "Imie Nazwisko"
  echo          git config --global user.email "login@users.noreply.github.com"
  exit /b 1
)
git add -A 2> "%TEMP%\fth_add_err.txt"
if errorlevel 1 (
  type "%TEMP%\fth_add_err.txt"
  del "%TEMP%\fth_add_err.txt" >nul 2>&1
  exit /b 1
)
del "%TEMP%\fth_add_err.txt" >nul 2>&1
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
if not defined MSG set "MSG=build(android): !BUILD_TYPE! !TS!"
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
  echo        Jesli git pyta o haslo, wykonaj raz: gh auth setup-git
  exit /b 1
)
echo [OK] Wyslano !SHA:~0,7! na origin/!BRANCH!
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
echo        Ostatnie linie:
powershell -NoProfile -Command "Get-Content -LiteralPath (Join-Path $env:OUT_DIR 'build-failed.log') -Tail 40"
goto :fail


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
echo    debug              ^(domyslnie^) commit + push + build debug w GitHub Actions + pobranie APK
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
REM  Ponizej: wbudowana kopia .github\workflows\android-hub-build.yml.
REM  cmd nigdy nie wykonuje tych linii - czyta je tylko ensure_workflow_file.
REM ==========================================================================
goto :eof
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
          if [ "$USE_WRAPPER" = "true" ]; then
            chmod +x ./gradlew
            GRADLE=./gradlew
          else
            GRADLE=gradle
          fi
          tasks=("assemble${VARIANT}" "test${VARIANT}UnitTest")
          if [ "$RUN_LINT" = "true" ]; then tasks+=("lint${VARIANT}"); fi
          "$GRADLE" --no-daemon --stacktrace "${tasks[@]}"

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
