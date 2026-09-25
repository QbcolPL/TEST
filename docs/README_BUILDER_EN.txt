FIELD TAK HUB 2.3.0 — UI 2.4 BETA
BUILDER GUIDE — PREPARING AN .FTAK PACKAGE
ENGLISH VERSION

0. BUILDER 2.4 UI

Builder 2.3.0 keeps the 2.3 package/build engine, while the main window follows the Builder 2.4 BETA concept: left navigation, central package wizard, right status panel, and tiled ATAK/Plugins/Maps/Overlays/Meshtastic/Server content selection.

The top-right corner provides a quick PL / EN language switch. It applies immediately without opening Settings.

The “Select recommended profile” action applies the baseline MILSIM values. Each “Configure” action switches to Advanced mode and exposes the relevant detailed section.

1. PURPOSE

Field TAK Hub Builder 2.3.0 is used to prepare a signed .ftak package for ATAK, OpenTAKServer and Meshtastic deployments.

Core principle:

    THE ORGANIZER PREPARES.
    THE PARTICIPANT SCANS.
    FIELD TAK HUB DOES THE REST.

The Builder works from the source/ directory. The out/ directory is the output directory. Do not place input files directly into out/.

2. CREATE A PROJECT

1. Start Field TAK Hub Builder 2.3.0.
2. Select New Project.
3. Enter a name, for example:

    GGZS-TAK-Test-Meshtastic

4. The Builder creates the project under:

    Documents\FieldTAKHub\Projects\<ProjectName>\

Example structure:

    GGZS-TAK-Test-Meshtastic\
    |-- GGZS-TAK-Test-Meshtastic.fthproj
    |-- README.md
    |-- source\
    `-- out\

3. WHERE TO PLACE FILES

Use these directories under source/:

    source\atak\
        ATAK Mission Package, .pref files and other ATAK-imported data.

    source\plugins\
        ATAK plugin APK files.

    source\maps\
        maps and map databases such as MBTiles, GeoPackage and SQLite.

    source\overlays\
        KML/KMZ overlays.

    source\config\
        additional configuration files.

    source\data\
        other deployment data.

    source\meshtastic\
        Meshtastic configuration.

    source\enrollment\
        optional ATAK/OTS enrollment URI.

4. PACKAGE INFORMATION

Set:

Package ID:
    e.g. pl.ggzs.tak.test

Name:
    e.g. GGZS TAK Test

Version:
    e.g. 2.3.0-test.1

Publisher:
    author or organization name

5. OPENTAKSERVER

Example values for the current test environment:

Host:
    ggzstak.duckdns.org

CoT:
    8089

API / Enrollment:
    8446

Web:
    8443

Enter only the domain name in Host. Do not add https://.

6. BUILDER MODE

Version 2.3.0 provides two modes:

SIMPLE MODE
    For a standard package without administrative configuration.

ADVANCED MODE
    For administrators and testing. It exposes Meshtastic settings, device role, MQTT/TLS gateway settings, enrollment and source/ content.

Use ADVANCED MODE for Meshtastic test packages.

7. MESHTASTIC — PREPARE THE CHANNEL

You need a canonical Meshtastic channel link, for example:

    https://meshtastic.org/e/...

Paste it into Meshtastic Channel URL and select Save.

The Builder stores it as:

    source\meshtastic\channel.url

SECURITY NOTE:
A Meshtastic channel URL may contain information that allows access to a protected channel. Treat it as sensitive information. Do not publish it in a public repository or public documentation.

8. MESHTASTIC — BASIC SETTINGS

Open:

    Advanced Mode -> Meshtastic

For the baseline test package use:

Mode:
    HYBRID

Device Role:
    FIELD NODE

Device Model:
    actual hardware model, e.g. tbeam_supreme

Channel Name:
    GGZS-TAK

Region:
    EU_868

Modem Preset:
    SHORT_FAST

Hop Limit:
    3

PLI:
    ON

GeoChat:
    ON

OTS Relay:
    ON

File Transfer:
    OFF

9. UNIVERSAL MESHTASTIC HARDWARE

Field TAK Hub is hardware-neutral. Device Model is descriptive metadata, not a per-device driver.

Example values:

    tbeam
    tbeam_supreme
    heltec_v3
    heltec_v4
    seeed_meshtracker_x1
    seeed_xiao_esp32s3
    seeed_xiao_nrf52840
    rak4631
    generic_meshtastic

Switching from Heltec to T-Beam or SenseCAP does not require a different .ftak architecture.

10. FIELD NODE

For a normal field radio use:

    Device Role: FIELD NODE (Meshtastic TAK)
    Gateway Enabled: OFF

Example for T-Beam Supreme:

    Mode: HYBRID
    Device Role: FIELD NODE (Meshtastic TAK)
    Device Model: tbeam_supreme
    Channel Name: GGZS-TAK
    Region: EU_868
    Modem Preset: SHORT_FAST
    Hop Limit: 3
    PLI: ON
    GeoChat: ON
    OTS Relay: ON
    File Transfer: OFF
    Gateway Enabled: OFF

11. MESHTASTIC <-> OPENTAKSERVER GATEWAY

The gateway is infrastructure. Example topology:

    T-Beam / SenseCAP X1
            |
           LoRa
            v
       Heltec V3
            |
          Wi-Fi
            |
      MQTT/TLS :8883
            v
      OpenTAKServer
            |
        CoT SSL :8089
            v
           ATAK

For the gateway use:

    Device Role: GATEWAY

Example values:

    Gateway Enabled: ON
    Gateway Type: heltec_v3
    Gateway Role: ots_mqtt
    Gateway Transport: mqtt_tls
    MQTT Port: 8883
    Root Topic: opentakserver
    Secrets External: ON

Heltec V3 is an example gateway, not a project requirement. Another compatible Meshtastic device may be used as the gateway.

12. SECRETS — NEVER PLACE THESE IN .FTAK

Do NOT put these into the project or package:

    - MQTT passwords,
    - permanent production tokens,
    - TLS private keys,
    - publisher private keys,
    - other persistent credentials.

Keep:

    Secrets External: ON

The project validator enforces this requirement.

13. ENROLLMENT

Optionally provide:

    source\enrollment\enroll.url

For testing, prefer short-lived or one-time enrollment tokens. Do not publish an enrollment URI with the public repository if it contains a secret.

14. ANALYZE

After copying all files, select Analyze.

Check that the Builder detects:

    - ATAK Mission Package,
    - APK plugins,
    - maps,
    - overlays,
    - data,
    - Meshtastic configuration.

For Meshtastic, verify:

    source\meshtastic\channel.url

15. BUILD PACKAGE

Select Build Package.

Before confirming, check:

    - package name,
    - version,
    - OTS server,
    - CoT 8089,
    - API/Enrollment 8446,
    - Web 8443,
    - Package validity,
    - QR validity,
    - file count.

After confirmation the output will be written to:

    out\<package-name>.ftak

16. WHAT GOES INTO .FTAK

In simplified form:

    .ftak
    |-- META-INF\
    |   |-- fieldtak.json
    |   |-- checksums.sha256
    |   |-- signature.ed25519
    |   `-- server.txt
    `-- payload\
        |-- atak\
        |-- plugins\
        |-- maps\
        |-- overlays\
        |-- config\
        |-- data\
        |-- meshtastic\
        |   |-- channel.url
        |   `-- profile.json
        `-- enrollment\

profile.json is generated by the Builder from the profile settings.

17. PACKAGE VALIDITY AND QR VALIDITY

Package validity controls how long the .ftak package remains valid.

QR validity controls how long the QR/distribution address remains valid.

Example:

    Package validity: 6 h
    QR validity: 6 h
    Max downloads: 50

18. FINAL CHECK BEFORE SENDING TO TESTERS

    [ ] .ftak is in out/
    [ ] correct version
    [ ] correct OTS Host
    [ ] CoT = 8089
    [ ] API/Enrollment = 8446
    [ ] Web = 8443
    [ ] correct Channel URL
    [ ] correct Device Role
    [ ] correct Device Model
    [ ] Gateway OFF for Field Node
    [ ] Gateway ON only for a gateway
    [ ] no MQTT credentials
    [ ] no private keys
    [ ] Channel URL treated as sensitive data

19. RECOMMENDED FIRST 2.3.0 TEST

For the first test deployment use:

    Gateway:
        Heltec V3

    Field Nodes:
        T-Beam Supreme
        SenseCAP MeshTracker X1

Then verify the full path:

    LoRa -> Gateway -> MQTT/TLS -> OpenTAKServer -> ATAK

20. PROJECT PRINCIPLE

Field TAK Hub 2.3.0 is designed as a hardware-neutral Meshtastic deployment tool. Heltec V3 is an example infrastructure gateway, not a project limitation.

End of guide.


## ATAK and Meshtastic app installation

A `.ftak` package may bundle `payload/atak/atak.apk`, `payload/apps/meshtastic.apk` and `payload/plugins/*.apk`. During **Prepare phone**, Field TAK Hub checks and launches installation/update for ATAK, the bundled Meshtastic app and then plugins. Android requires user confirmation. The Hub does not silently install applications or download missing APKs. Builder source locations are `source/atak/*.apk`, `source/apps/meshtastic.apk` and `source/plugins/*.apk`.
