# Field TAK Hub 2.3.0 — Release/Test Checklist

## CI / build

- [ ] `python tests/full_static_gate.py` passes
- [ ] Android unit tests pass
- [ ] Android `assembleDebug` passes
- [ ] Android release build passes on GitHub Actions
- [ ] Builder unit tests pass on Windows
- [ ] Builder `dotnet publish` passes
- [ ] release workflow generates manifest and SHA-256 checksums

## Android functional tests

- [ ] Simple Mode layout
- [ ] Advanced Mode layout
- [ ] `.ftak` import
- [ ] provisioning QR
- [ ] enrollment QR
- [ ] Data Package QR
- [ ] Smart Diagnosis
- [ ] Recovery
- [ ] back navigation
- [ ] double-back on home screen
- [ ] Polish / English resources
- [ ] author-support panel / BuyCoffee link

## Meshtastic

- [ ] app missing
- [ ] app installed without channel configuration
- [ ] prepared `channel.url`
- [ ] optional `profile.json`
- [ ] phone-to-radio connection
- [ ] radio disconnected state
- [ ] handoff to official Meshtastic app/plugin
- [ ] field node workflow
- [ ] gateway metadata/diagnostics

## End-to-end

- [ ] PLI
- [ ] GeoChat
- [ ] LoRa -> gateway
- [ ] gateway -> MQTT/TLS
- [ ] MQTT -> OpenTAKServer
- [ ] OpenTAKServer -> ATAK

## Security

- [ ] no secrets committed
- [ ] no private Channel URL in public example data
- [ ] enrollment secrets are not logged or persisted by Android
- [ ] MQTT credentials remain outside `.ftak`
