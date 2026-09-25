# Update System

Field TAK Hub updates are deliberately **user-approved**. There is no silent updater.

## Release channels

`version.json` is the canonical project version/channel source:

```json
"hubVersion": "2.3.0",
"builderVersion": "2.3.0",
"releaseChannel": "stable"
```

- `rc` clients can use GitHub prereleases.
- `stable` clients use normal non-prerelease releases.

The updater queries the GitHub Releases list rather than `/releases/latest`, so prerelease handling remains explicit.

## Release manifest

A GitHub release can contain `fieldtak-release.json` with the exact APK and Builder artifact hashes. The release workflow generates SHA-256 values from the actual files produced by CI.

## Android flow

1. Hub checks the configured GitHub repository.
2. It discovers releases for the selected channel.
3. It downloads the release manifest over HTTPS.
4. When a newer Hub is available, the UI offers an update.
5. The APK is downloaded and verified with SHA-256.
6. Android's normal installer asks the user to confirm installation.

Redirects are revalidated as HTTPS.

## Builder flow

1. Builder checks for updates without interrupting the operator.
2. The operator may start a manual update check.
3. The downloaded artifact is verified with SHA-256.
4. Builder opens the verified file for the user; it does not silently self-replace.

## Repository setting

The default repository remains `Qbcol/TAKFieldHub` in `version.json`.
