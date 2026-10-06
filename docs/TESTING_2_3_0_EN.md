# Field TAK Hub 2.3.0 — test guide

## Goal

Verify the Android application, Windows Builder and the complete ATAK / OpenTAKServer / Meshtastic workflow.

## 1. Android application

1. Launch Field TAK Hub.
2. Verify Polish / English resources.
3. In Simple Mode verify:
   - Prepare phone,
   - Import `.ftak`,
   - Device status,
   - Check everything,
   - Configure radio when Meshtastic configuration is present.
4. Open Advanced Mode.
5. Verify the dedicated OpenTAKServer sections:
   - User enrollment from QR,
   - Data Packages from QR.
6. Verify diagnostics and history.
7. Verify the author-support panel and BuyCoffee link.

## 2. Back navigation

- Inside the app, the first back action returns to the previous screen.
- On the home screen, a single back action does not close the app.
- A second back action within 2 seconds moves the app to the background.

## 3. `.ftak`

Test:

- local `.ftak` import;
- signature verification;
- expired package handling;
- missing required files;
- correct handoff to ATAK.

## 4. QR

Test all three main QR workflows:

- phone provisioning;
- user enrollment;
- Data Package import.

An incorrect QR type should be rejected with a readable message.

## 5. Meshtastic

Test at least:

- Meshtastic app missing;
- app installed without a prepared channel;
- package containing `channel.url`;
- package containing a Meshtastic profile;
- radio connected;
- radio disconnected;
- handoff to the official Meshtastic app/plugin.

Reference hardware matrix:

- T-Beam Supreme;
- SenseCAP MeshTracker X1;
- Heltec as an infrastructure gateway.

## 6. End-to-end test

```text
Phone
  -> Radio
  -> LoRa
  -> Gateway
  -> MQTT/TLS
  -> OpenTAKServer
  -> ATAK
```

Verify PLI and GeoChat in a real radio environment.

## 7. Bug report

Include:

- app version;
- phone model;
- Android version;
- Meshtastic radio model;
- ATAK / plugin versions;
- reproduction steps;
- diagnostics output when available;
- GitHub Actions log for CI/build issues.

Do not attach passwords, private keys, tokens, private Channel URLs or secret-bearing enrollment URIs.
