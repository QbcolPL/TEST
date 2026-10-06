# Security model — Field TAK Hub 2.0 FINAL

## Threats addressed

- corruption in transit: descriptor whole-package SHA-256 + signed per-file SHA-256 list
- malicious modification of payload: Ed25519 signature
- extra unsigned payload injection: verifier compares actual payload files with the signed checksum list
- path traversal: canonical-path Zip Slip checks
- accidental trust in a new signer: fingerprint + explicit local trust
- theft of Builder signing-key blob at rest: Windows DPAPI/current-user scope
- package URL reuse: random bearer token + expiry + optional download-count limit
- interrupted download: `.part` + HTTP Range, followed by complete SHA-256 validation before extraction
- accidental backup of local deployment state: Android Auto Backup disabled

## Trust boundary

The LAN/HTTPS descriptor tells the Hub **where** to fetch a bundle. The descriptor itself is not treated as authority for package contents. The downloaded `.ftak` still has to pass Ed25519, file-list and SHA-256 validation and its publisher must be trusted locally.

## Android constraints

Field TAK Hub does not bypass Android's package installer, does not silently install APKs on unmanaged devices and does not write into ATAK private `Android/data` storage.

The app may compare installed plugin packages with APKs in the bundle. Controlled/sideload builds use `QUERY_ALL_PACKAGES` for this because arbitrary plugin package IDs cannot be known at Hub build time. A public Play Store distribution would require separate policy/design review.

## OpenTAK verification boundary

DNS and TCP reachability can be tested externally. ATAK's private certificate store and its authenticated CoT session cannot be reliably introspected by another ordinary Android app. These checks are therefore represented as `UNKNOWN` instead of fabricated success.

## Future managed mode

Hardware-backed Builder keys, publisher-key revocation, official ATAK telemetry/helper plugin, MDM/device-owner install and per-device certificate enrollment remain suitable later 2.x extensions.
