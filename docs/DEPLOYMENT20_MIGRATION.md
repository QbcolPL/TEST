# Field TAK Hub 2.3.5 — Deployment 2.0 migration

This development package keeps the `.ftak` schema at v2 and adds a Builder-side migration path for older `.ftak` packages that do not contain a `deployment` policy.

## Workflow

1. Open Builder 2.3.5.
2. Use **Import .FTAK** for ordinary editing, or **Convert to Deployment 2.0** for an explicit migration.
3. The converter validates that `META-INF/fieldtak.json` exists and reads the existing package metadata.
4. If `deployment` exists, it is preserved.
5. If it is missing, the converter infers only what can be established from the package metadata:
   - ATAK is required.
   - Plugins are required only when plugins are present.
   - OpenTAKServer is required when a server host is configured.
   - Enrollment is required when enrollment is enabled.
   - Meshtastic, Mission Package, maps and overlays remain optional unless an existing Deployment 2.0 policy explicitly says otherwise.
6. The converted project receives `DEPLOYMENT20-CONVERSION-REPORT.txt`.
7. Build Package creates a new signed `.ftak` v2. The original package is never modified and its old signature is not copied to the new package.

## Android compatibility

Android 2.3.5 reads the `deployment` object when present. Older `.ftak` packages remain importable; packages without the object use Android's compatibility defaults.

## Security

The converter does not copy the old package signature into the new package. New packages are signed with the current Builder publisher key. Do not place reusable enrollment secrets, MQTT credentials or private signing keys in the project.
