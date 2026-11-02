@echo off
setlocal EnableExtensions
cd /d "%~dp0.."

echo ============================================================
echo FIELD TAK HUB 2.3.0 - LOCAL BUILD CHECK
echo ============================================================
echo.

echo [1/2] Static gate...
where python >nul 2>&1
if errorlevel 1 (
  echo BRAK PYTHON. Pomijam static gate.
) else (
  python tests\full_static_gate.py
  if errorlevel 1 goto :FAIL
)

echo.
echo [2/2] Windows Builder...
call tools\BUILD_WINDOWS_BUILDER_LOCAL_v3.cmd
if errorlevel 1 goto :FAIL

echo.
echo Android:
echo   Otworz apps\android w Android Studio i uruchom Gradle Sync / assembleDebug.
echo.
echo GOTOWE.
exit /b 0

:FAIL
echo.
echo BUILD CHECK FAILED.
pause
exit /b 1
