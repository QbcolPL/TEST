# Architecture

```text
Windows Builder
  ├─ ProjectService (.fthproj)
  ├─ SourceAnalyzer
  ├─ MissionPackageBuilder
  ├─ FtakPackageBuilder
  │    ├─ SHA-256
  │    └─ Ed25519 / DPAPI key
  └─ DistributionServer + QR
             │
             ▼
          .ftak v2
             │
             ▼
Android Hub
  ├─ QR Scanner
  ├─ Downloader / SAF importer
  ├─ FtakVerifier
  ├─ PublisherTrustStore
  └─ ProvisioningController
       ├─ APK → system package installer
       └─ mission-package.zip → content URI / ATAK

Meshtastic / OTS path:

```text
Field node (T-Beam / SenseCAP X1)
        │
        │ LoRa
        ▼
Heltec WiFi LoRa 32 V3 (gateway)
        │
        │ MQTT/TLS :8883
        ▼
OpenTAKServer
        │
        │ CoT SSL :8089
        ▼
ATAK / Field TAK Hub
```

Field TAK Hub describes and diagnoses this topology but does not own the gateway BLE/radio connection. Radio I/O stays with Meshtastic / ATAK plugin; the public `.ftak` never carries MQTT credentials or private keys.
```

## Boundary decisions

The Android app does not depend on private ATAK APIs and does not write into another application's private/scoped-storage directory. This makes Field TAK Hub usable without root and avoids coupling the project to one ATAK build.

## Future adapters

`ProvisioningController` is deliberately small so a later 2.x release can add:

- an official ATAK plugin bridge,
- enrollment API support,
- device-owner/MDM mode,
- per-device package issuance,
- fleet state reporting.
