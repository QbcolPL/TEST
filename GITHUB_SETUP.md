# GitHub Setup — Field TAK Hub 2.3.0

## 1. Create the repository

Recommended repository name:

```text
field-tak-hub
```

Do not upload APK/EXE binaries or private deployment data to the source repository.

## 2. Push the source

```bash
git init
git branch -M main
git add .
git commit -m "Field TAK Hub 2.3.0"
git remote add origin https://github.com/Qbcol/TAKFieldHub.git
git push -u origin main
```

If the repository name or owner is different, update `version.json` and the updater configuration consistently.

## 3. GitHub Actions

After the first push, verify these workflows:

- **Source Gate**
- **Android**
- **Windows Builder**

The release workflow is triggered by a version tag.

## 4. Release signing secrets

For Android release builds configure:

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`

For optional Windows Authenticode signing configure:

- `WINDOWS_CERT_BASE64`
- `WINDOWS_CERT_PASSWORD`

See [BUILD_AND_SIGN.md](docs/BUILD_AND_SIGN.md).

## 5. 2.3.0 release tag

The release workflow expects tags matching `v*`. For the current version use:

```bash
git tag -a v2.3.0 -m "Field TAK Hub 2.3.0"
git push origin v2.3.0
```

The release workflow generates the Android APK, Windows Builder package, release manifest and SHA-256 checksums.
