$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\.."
$android=Join-Path $root 'apps\android'
$bootstrap=Join-Path $android '.gradle-bootstrap'
$version='9.6.0'
$zip=Join-Path $bootstrap "gradle-$version-bin.zip"
$home=Join-Path $bootstrap "gradle-$version"
New-Item -ItemType Directory -Force -Path $bootstrap | Out-Null
if(-not (Test-Path "$home\bin\gradle.bat")) {
  Write-Host "Downloading Gradle $version..."
  Invoke-WebRequest "https://services.gradle.org/distributions/gradle-$version-bin.zip" -OutFile $zip
  Expand-Archive $zip -DestinationPath $bootstrap -Force
}
Push-Location $android
try {
  & "$home\bin\gradle.bat" wrapper --gradle-version $version
} finally { Pop-Location }
Write-Host "Gradle wrapper generated. You can now run apps\android\gradlew.bat assembleDebug (or scripts\build-android.ps1 -Release)"
