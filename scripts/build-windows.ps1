$ErrorActionPreference='Stop'
$root=Resolve-Path "$PSScriptRoot\.."
$out=Join-Path $root 'artifacts\windows\FieldTakHub.Builder'
New-Item -ItemType Directory -Force -Path $out | Out-Null
dotnet publish "$root\apps\builder\FieldTakHub.Builder\FieldTakHub.Builder.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
Write-Host "Windows Builder: $out"
