# Build and Signing — Field TAK Hub 2.3.0

## Windows Builder

Requirements:

- Windows 10/11 x64;
- .NET 10 SDK.

Build/publish:

```powershell
.\scripts\build-windows.ps1
```

The script publishes a self-contained `win-x64` Builder under `artifacts/windows/`.

### Authenticode

Windows signing is optional in the normal test workflow and can be enabled in GitHub Actions with:

- `WINDOWS_CERT_BASE64`
- `WINDOWS_CERT_PASSWORD`

## Android

Requirements:

- JDK 17;
- Android SDK / compileSdk 37 as configured by the project;
- Gradle 9.6.

For local release signing, create `apps/android/keystore.properties` from the example:

```properties
storeFile=../release.jks
storePassword=...
keyAlias=...
keyPassword=...
```

Then:

```powershell
cd apps\android
gradle testDebugUnitTest assembleRelease
```

### GitHub Actions signing secrets

The release workflow uses:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`

The keystore is supplied only to the CI runner and is not part of the repository.

## Source validation

Run the complete static validation locally:

```bash
python tests/full_static_gate.py
```

The individual Android contracts can also be run directly from `tests/`.

## Release

Create the 2.3.0 release tag only after the external test cycle is ready:

```bash
git tag -a v2.3.0 -m "Field TAK Hub 2.3.0"
git push origin v2.3.0
```
