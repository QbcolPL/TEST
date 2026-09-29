# Field TAK Hub 2.3.0 — Release Guide

## Release identity

- Hub: 2.3.0
- Builder: 2.3.0
- Android versionCode: 23002
- channel: stable
- `.ftak`: schema v2
- project: schema v2
- storage: schema v1

## Pre-release gate

```powershell
python tests/source_gate.py
python tests/meshtastic_hybrid_contract.py
python tests/meshtastic_gateway_contract.py
python tests/android_simple_optional_skip_contract.py
```

## GitHub release

```bash
git add .
git commit -m "Field TAK Hub 2.3.0 / Builder 2.3.0"
git tag -a v2.3.0 -m "Field TAK Hub 2.3.0"
git push origin main --tags
```

Tag `v2.3.0` triggers the release workflow and produces the Android APK, Windows Builder ZIP, release manifest and SHA256SUMS.

## Final source validation

The final source package includes the Android `meshRuntime` CI compile fix and refreshed Builder documentation/workspace README generation. Source Gate and Meshtastic contract checks pass in the available environment.

## Important

An APK/EXE should only be distributed as a binary after the final GitHub Actions Android/Windows workflows complete successfully.


### FINAL UX corrections

- Added Simple/Advanced mode separation to Android Hub and Windows Builder.
- Simple mode is the default for field deployment.
- Added separate **Package validity** and **QR validity** settings in Builder.
- Existing projects remain compatible: missing `QrExpiryHours` defaults to 6 hours and missing `BuilderMode` defaults to Simple.


## Meshtastic gateway integration

Version 2.3 keeps the HYBRID architecture and explicitly describes the infrastructure gateway in the `.ftak` manifest/profile:

`field node → LoRa → Heltec WiFi LoRa 32 V3 → MQTT/TLS → OpenTAKServer → ATAK`

The package carries only non-secret gateway metadata (type, role, transport, MQTT port and Root Topic). MQTT passwords, tokens, certificates and private keys remain external to `.ftak`. Android diagnostics can test the OpenTAKServer MQTT/TLS TCP endpoint, but this does not prove that the physical Heltec radio is connected to the LoRa mesh.

## UX refresh + Meshtastic one-tap setup

The 2.3.0 test build now simplifies Simple Mode around three actions:
- Check Everything
- Configure Radio
- Prepare Phone

A readiness banner explains the next useful action instead of exposing deployment terminology.

The new `Configure Radio` action starts the supported Meshtastic hand-off:
- if the Meshtastic Android app is installed, Field TAK Hub opens the prepared `channel.url`;
- if the direct Meshtastic ATAK plugin is detected and the app path is unavailable, Hub opens ATAK for the plugin workflow;
- Hub does not claim that BLE/USB/profile writes succeeded unless the supported Meshtastic path reports the connection state.

This keeps Field TAK Hub hardware-neutral and avoids implementing a separate BLE/USB driver for every Meshtastic radio.

## Diagnostic logging

Field TAK Hub 2.3.0 stores a local diagnostic log for troubleshooting during field tests. The log is exposed only in Advanced Mode and can be exported as a TXT file. It records operational events and failures, but intentionally redacts sensitive enrollment, Meshtastic channel and credential material.

Workflow: **problem → Smart Diagnosis → Recovery → Advanced Mode → Diagnostic Logs → Export Diagnostic Log**.
