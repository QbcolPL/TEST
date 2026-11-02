# Build Field TAK Hub 2.0 FINAL

## Windows Builder

Install .NET 10 SDK on Windows 10/11 x64.

```powershell
.\scripts\build-windows.ps1
```

Output:

```text
artifacts/windows/FieldTakHub.Builder/
```

## Android

Requirements: JDK 17 and Android SDK API 37.

The repository intentionally supports two build paths:

1. GitHub Actions uses an explicitly installed Gradle 9.6 distribution.
2. A local Windows workstation runs `scripts/bootstrap-android.ps1` once to download Gradle and generate the wrapper JAR, then `scripts/build-android.ps1`.

```powershell
.\scripts\bootstrap-android.ps1
.\scripts\build-android.ps1
```

Output:

```text
artifacts/android/FieldTakHub-2.0.0-FINAL-debug.apk
```

For a signed release copy `apps/android/keystore.properties.example` to `keystore.properties`, point it at your own keystore, and never commit that file or keystore.

## Everything

```powershell
.\scripts\build-all.ps1
```

## CI

- `.github/workflows/android.yml` runs unit tests and `assembleDebug`.
- `.github/workflows/windows-builder.yml` restores/builds/publishes the WPF Builder.
- `.github/workflows/release.yml` creates release artifacts when a `v*` tag is pushed.
