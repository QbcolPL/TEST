@echo off
setlocal
cd /d "%~dp0.."
echo === Field TAK Hub 2.3.0 RC1 - build helper ===
where dotnet >nul 2>&1 || (echo ERROR: .NET SDK 10 is required.&exit /b 1)
where java >nul 2>&1 || (echo ERROR: Java 17+ is required.&exit /b 1)

echo [1/2] Building Windows Builder...
dotnet restore apps\builder\FieldTakHub.Builder.sln || exit /b 1
dotnet build apps\builder\FieldTakHub.Builder.sln -c Release --no-restore || exit /b 1

echo [2/2] Building Android...
if not exist apps\android\gradle\wrapper\gradle-wrapper.jar (
  echo NOTE: gradle-wrapper.jar is not included in this source package.
  echo       Open apps\android in Android Studio and let Gradle sync/download its wrapper.
)
call apps\android\gradlew.bat :app:assembleDebug || (
  echo If the wrapper JAR is missing, open apps\android in Android Studio and sync the project.
  exit /b 1
)

echo BUILD COMPLETE
endlocal
