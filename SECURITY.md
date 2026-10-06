# Security policy

## Supported branch

Security fixes target the current `2.x` line.

## Important operational rules

- Do not commit real `.p12`, `.pfx`, private keys, passwords, enrollment tokens, or production server secrets.
- Treat QR links as bearer capabilities until they expire.
- Prefer short token lifetimes when packages contain credentials.
- Verify a publisher fingerprint out-of-band before choosing **Trust publisher**.
- Prefer per-device/client certificates instead of sharing one client certificate across many devices.
- Keep the Windows account that owns the Builder signing key protected.
- Rotate signing keys deliberately; a new key results in a new publisher fingerprint.

## Reporting

Do not post live credentials in public issues. Open a private security advisory in GitHub or contact the repository maintainer privately.
