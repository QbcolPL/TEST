# Field TAK Hub 2.3.0 — EN

**The organizer prepares. The participant scans. Field TAK Hub does the rest.**

Field TAK Hub prepares an Android phone for ATAK, OpenTAKServer and Meshtastic. Version 2.3.0 focuses on a simple field workflow, full administrator tooling and practical diagnostics.

## Main features

- signed `.ftak` package import;
- phone provisioning from QR;
- user enrollment from QR;
- Data Package import from QR;
- Simple Mode and Advanced Mode;
- Smart Diagnosis and Recovery;
- Meshtastic HYBRID and prepared-configuration handoff;
- phone-to-radio status diagnostics;
- hardware-neutral Meshtastic support;
- Polish / English UI;
- author support panel with BuyCoffee;
- GitHub Actions CI.

## Documentation

- [Android user guide](docs/README_USER_EN.txt)
- [Builder guide](docs/README_BUILDER_EN.txt)
- [2.3.0 test guide](docs/TESTING_2_3_0_EN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Meshtastic HYBRID](docs/MESHTASTIC_HYBRID.md)
- [`.ftak` format](docs/FTAK_FORMAT.md)
- [Security model](docs/SECURITY_MODEL.md)

## Meshtastic architecture

```text
Phone -> Meshtastic Radio -> LoRa -> Gateway -> MQTT/TLS -> OpenTAKServer -> ATAK
```

Field TAK Hub delivers the prepared configuration but does not replace the Meshtastic BLE/USB or radio layer.

## Important

Do not publish passwords, private keys, tokens, private Meshtastic Channel URLs or secret-bearing enrollment URIs.
