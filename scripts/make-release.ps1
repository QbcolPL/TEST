$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\.."
& "$PSScriptRoot\run-source-gate.ps1"
& "$PSScriptRoot\build-windows.ps1"
Write-Host "For the signed Android release use GitHub Actions or configure apps/android/keystore.properties and run: scripts/build-android.ps1 -Release"
Write-Host "RC release artifacts are intentionally produced by the tag workflow so fieldtak-release.json contains hashes of the exact published files."
