FIELD TAK HUB 2.3.0
USER GUIDE — ANDROID
ENGLISH VERSION

1. WHAT IS FIELD TAK HUB?

Field TAK Hub prepares an Android phone for ATAK, OpenTAKServer and Meshtastic deployments.

It is designed to avoid manual entry of many settings.

Core principle:

    THE ORGANIZER PREPARES.
    THE PARTICIPANT SCANS.
    FIELD TAK HUB DOES THE REST.

Field TAK Hub does not directly own the Meshtastic BLE/USB or radio link. Radio communication remains with the Meshtastic application and the appropriate plugin.

2. WHAT DO YOU NEED?

Depending on the deployment you need:

    - an Android phone,
    - Field TAK Hub,
    - ATAK CIV,
    - the Meshtastic application,
    - an .ftak package or the appropriate QR code from the organizer.

3. FIRST START

Start Field TAK Hub.

Version 2.3.0 has two modes:

SIMPLE MODE
    Default mode for field users.

ADVANCED MODE
    For administrators, testers and diagnostics.

Normal field users should stay in SIMPLE MODE.

4. QR — PHONE PROVISIONING

On the home screen choose the phone provisioning scan action.

Scan the QR code provided by the organizer.

Field TAK Hub will start the appropriate workflow defined by the deployment package.

5. QR — ENROLLMENT

If the organizer provided an enrollment QR:

    Scan enrollment

Point the camera at the code and wait for the process to finish.

Enrollment may be short-lived or one-time. Do not publish the received URI.

6. QR — DATA PACKAGE

If the organizer provided a Data Package QR:

    Scan Data Package

Scan the code and wait for the import to ATAK.

The application distinguishes the intended QR workflows and should not run a different workflow when the wrong QR type is scanned.

7. IMPORT AN .FTAK PACKAGE

If you receive:

    package-name.ftak

select:

    Import package

and choose the .ftak file.

Do not manually edit the .ftak package.

8. WHAT CAN THE PACKAGE CONTAIN?

Depending on the deployment the package may include:

    - ATAK configuration,
    - plugins,
    - maps,
    - overlays,
    - data,
    - Meshtastic configuration,
    - OpenTAKServer information,
    - enrollment data.

Meshtastic content may include:

    payload/meshtastic/channel.url
    payload/meshtastic/profile.json

9. MESHTASTIC — WHAT DOES THE USER NEED TO DO?

When the administrator prepared the package correctly, there is no need to manually re-enter channel parameters.

Field TAK Hub delivers the prepared configuration. The user then works with the Meshtastic application, the ATAK plugin and the radio.

If Meshtastic is optional for the deployment, SIMPLE MODE provides **SKIP MESHTASTIC** / **SKIP OPTIONAL STEP**. Skipping is not an error; the user can complete the task later in ADVANCED MODE.

10. EXAMPLE COMMUNICATION PATH

    Phone
      |
      +-- ATAK
      +-- Meshtastic
      +-- Meshtastic plugin
              |
              v
            Radio
              |
             LoRa
              |
              v
           Gateway
              |
          MQTT/TLS
              |
              v
      OpenTAKServer
              |
             CoT
              |
              v
            ATAK

An example infrastructure gateway is Heltec V3.

11. FIELD NODE VS GATEWAY

A typical field user operates as:

    FIELD NODE

Examples:

    - T-Beam,
    - T-Beam Supreme,
    - SenseCAP MeshTracker X1,
    - other compatible Meshtastic radios.

The gateway is infrastructure. Users should not change their device to Gateway unless instructed by the administrator.

12. IS FIELD TAK HUB ONLY FOR HELTEC?

No.

Field TAK Hub 2.3.0 is designed as a hardware-neutral Meshtastic deployment tool.

The same architecture can cover:

    T-Beam
    T-Beam Supreme
    Heltec
    SenseCAP MeshTracker X1
    Seeed/XIAO
    RAK
    other compatible Meshtastic devices

13. WHAT DOES THE MESHTASTIC STATUS MEAN?

The Meshtastic status may indicate:

    - that the application is installed,
    - that configuration is available,
    - that something still needs to be checked,
    - that an error occurred.

The fact that Meshtastic is installed does NOT by itself prove that the radio is connected or that the complete radio path is working.

14. “TEST MESHTASTIC” / CHECK MESHTASTIC

The “Check Meshtastic app” button is informational. It checks whether the Meshtastic app is installed and whether the package contains channel configuration. It does not automatically open Meshtastic and does not prove BLE/radio connectivity. Use the separate “Open Meshtastic” button to launch the app.

Do not treat it as proof of the full path:

    phone -> radio -> LoRa -> gateway -> MQTT -> OTS -> ATAK

A real end-to-end test requires the actual radio environment.

15. ADVANCED MODE

Advanced Mode is intended for:

    - administrators,
    - testers,
    - troubleshooting,
    - deployment operators.

It exposes additional information about configuration, OpenTAKServer, Meshtastic and the gateway.

16. OPENTAKSERVER DIAGNOSTICS

When the administrator asks you to run diagnostics:

    Advanced Mode -> Diagnostics / Service

For the current test environment the example Host is:

    ggzstak.duckdns.org

Host or port reachability alone does not prove that the whole end-to-end system is working.

17. GATEWAY DIAGNOSTICS

Diagnostics may test TCP reachability of the MQTT/TLS endpoint.

A successful TCP port test means only that the endpoint is reachable. It does not prove that the Heltec or another gateway radio is physically connected to the LoRa mesh.

18. QUICK CHECKLIST

    [ ] Field TAK Hub is installed
    [ ] ATAK is installed
    [ ] Meshtastic is installed when required
    [ ] correct .ftak or QR received
    [ ] package is not expired
    [ ] required network access is available
    [ ] radio is powered on
    [ ] radio is connected to the phone

19. WHAT TO DO IF SOMETHING DOES NOT WORK

1. Do not change gateway or server settings on your own.
2. Check that you have the correct .ftak/QR.
3. Check that Meshtastic and ATAK are installed/running.
4. Check the phone-to-radio connection.
5. Run diagnostics in Advanced Mode.
6. Send the diagnostic result to the administrator.

20. WHAT NOT TO DO

Do not enter or publish:

    - MQTT passwords,
    - private keys,
    - permanent tokens,
    - enrollment URIs containing secrets,
    - private Meshtastic channel URLs.

Do not manually edit the .ftak package.

21. SIMPLE USER INSTRUCTIONS

    1. Install Field TAK Hub.
    2. Install ATAK and Meshtastic when required by the organizer.
    3. Receive a QR code or .ftak package.
    4. Scan the QR code or import the .ftak package.
    5. Connect the Meshtastic radio to the phone when required.
    6. Start ATAK.
    7. Check the status.
    8. Ready — move to the field.

22. 2.3.0 TEST PLAN

For the first test cycle verify:

    - .ftak import,
    - all three QR types,
    - Meshtastic detection,
    - phone-to-radio connection,
    - PLI,
    - GeoChat,
    - LoRa -> gateway,
    - gateway -> MQTT/TLS,
    - MQTT -> OpenTAKServer,
    - OTS -> ATAK.

End of guide.


MESHTASTIC — live link status
Field TAK Hub listens to the optional Meshtastic Android status bridge. When Meshtastic reports a live radio connection, Hub shows “Meshtastic radio is connected”. When no supported status is available, Hub shows “Unverified” instead of guessing. Hub does not take ownership of BLE/USB or radio control.


## ATAK and Meshtastic app installation

A `.ftak` package may bundle `payload/atak/atak.apk`, `payload/apps/meshtastic.apk` and `payload/plugins/*.apk`. During **Prepare phone**, Field TAK Hub checks and launches installation/update for ATAK, the bundled Meshtastic app and then plugins. Android requires user confirmation. The Hub does not silently install applications or download missing APKs. Builder source locations are `source/atak/*.apk`, `source/apps/meshtastic.apk` and `source/plugins/*.apk`.


23. SKIPPING OPTIONAL STEPS

SIMPLE MODE provides skip buttons only for optional activities. The user can skip:

    - Meshtastic,
    - enrollment,
    - Data Package import,
    - Meshtastic radio configuration.

A skipped step is recorded as `SKIPPED`. It is not treated as an application failure. The skipped action can be completed later in ADVANCED MODE.

Required operations such as package integrity, publisher trust, required ATAK installation and required plugin installation cannot be skipped.
