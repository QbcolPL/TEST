param([switch]$Release)
$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\.."
if(-not (Test-Path "$root\apps\android\gradle\wrapper\gradle-wrapper.jar")) { & "$PSScriptRoot\bootstrap-android.ps1" }
Push-Location "$root\apps\android"
try {
  if($Release) { .\gradlew.bat clean testDebugUnitTest assembleRelease }
  else { .\gradlew.bat clean testDebugUnitTest assembleDebug }
} finally { Pop-Location }
$out="$root\artifacts\android"
New-Item -ItemType Directory -Force -Path $out | Out-Null
if($Release) {
  Copy-Item "$root\apps\android\app\build\outputs\apk\release\app-release.apk" "$out\FieldTAKHub-2.3.0.apk" -Force
} else {
  Copy-Item "$root\apps\android\app\build\outputs\apk\debug\app-debug.apk" "$out\FieldTAKHub-2.3.0-debug.apk" -Force
}
Write-Host "Android APK: $out"
