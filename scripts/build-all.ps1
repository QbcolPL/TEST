param([switch]$ReleaseAndroid)
$ErrorActionPreference='Stop'
& "$PSScriptRoot\run-source-gate.ps1"
& "$PSScriptRoot\build-windows.ps1"
& "$PSScriptRoot\build-android.ps1" -Release:$ReleaseAndroid
