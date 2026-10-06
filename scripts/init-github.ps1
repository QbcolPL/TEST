param([Parameter(Mandatory=$true)][string]$RemoteUrl)
$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\.."
Push-Location $root
try {
  if(-not (Test-Path '.git')) { git init }
  git add .
  if(-not (git diff --cached --quiet)) { git commit -m "Field TAK Hub 2.3.0 / Builder 2.3.0" }
  git branch -M main
  git remote remove origin 2>$null
  git remote add origin $RemoteUrl
  git push -u origin main
} finally { Pop-Location }
