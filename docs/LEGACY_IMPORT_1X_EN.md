# Import TAK Field Hub 1.x packages into the 2.x standard

Builder 2.3.0 provides **Import legacy 1.x ZIP**.

1. Select the original ZIP produced by a 1.x Builder.
2. Builder reads `manifest.json`.
3. If `manifest.sig` and `manifest.pub.pem` exist, the RSA/SHA-256 signature over the exact manifest bytes is verified.
4. Per-file SHA-256 values and `config.txt` integrity are verified. Any mismatch aborts import.
5. A new 2.x workspace is created.
6. ATAK APK becomes `source/atak/atak.apk`; plugins become `source/plugins/`. The legacy OpenTAK profile is mapped into the 2.x server fields.
7. `LEGACY-IMPORT-REPORT.txt` is written to the project root.
8. Click **Build package**. The new `.ftak` is signed with the current Ed25519 Publisher key.

The old RSA signature is not reused as a 2.x signature; it only authenticates the source ZIP before migration.
