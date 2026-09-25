# Administrator Guide

## Create a project

1. Start **Field TAK Hub Builder 2.1.0 RC6**.
2. Set the package name, package ID and package version.
3. Choose the source folder and output folder.
4. Set the ATAK minimum/maximum version.
5. Enter the TAK server profile in the GUI.
6. Click **Test server** before building.
7. Click **Analyze** and review the detected files.
8. Click **Build package** and review the Build Preview.
9. Confirm the build.

## Server profile

The default example is:

```text
Type: OpenTAK
Name: GGZS OpenTAK
Host: ggzstak.duckdns.org
CoT: 8089
API/Enrollment: 8446
Web: 8443
```

You may replace every field. The host field must contain only a DNS name or IP address — no `https://`, path or whitespace.

### server.txt

Builder can import/export a human-editable profile:

```text
SERVER_TYPE=OpenTAK
SERVER_NAME=GGZS OpenTAK
HOST=ggzstak.duckdns.org
COT_PORT=8089
API_PORT=8446
WEB_PORT=8443
```

Editing a sidecar `server.txt` does **not** modify an already-built `.ftak`; load it into Builder and rebuild.

## Source folder conventions

```text
source/
├── atak/        # optional ATAK APK + content that becomes ATAK Mission Package content
├── plugins/     # APK plugins
├── maps/        # map databases/packages
├── overlays/    # KML/KMZ etc.
├── config/
└── data/
```

If `source/atak/` contains one APK, Builder emits it as `payload/atak/atak.apk`. APK files are excluded from the Mission Package. If `source/atak/` contains any custom `.pref`, Builder keeps it; otherwise Builder creates a minimal `server.pref` from the GUI server profile.

## Publisher key

The signing key identifies your deployment publisher. Back it up before production use:

1. Open **Publisher security**.
2. Click **Export key backup**.
3. Choose a strong backup password (minimum 10 characters; a password manager-generated value is recommended).
4. Store the `.fthkey` offline.
5. Record the displayed fingerprint separately.

Restoring a backup replaces the Builder's local publisher key. Verify the fingerprint before the next production build.

## LAN distribution

1. Build the `.ftak`.
2. Click **Start LAN distribution**.
3. Allow the Builder through Windows Firewall on the private network if prompted.
4. Show the QR code to users.
5. Keep the Builder running until provisioning is complete.

The LAN server supports interrupted-download resume using HTTP Range/206. Its token expires and has a maximum download count.

## Phone workflow

The player should normally only need to:

1. install/open Field TAK Hub;
2. scan the QR;
3. trust the expected publisher fingerprint on first use;
4. tap **Finish configuration**;
5. approve Android APK installation prompts when required;
6. approve/import ATAK Mission Package content;
7. open ATAK and confirm the final authenticated connection.

Use **Diagnostics / Service mode** if a step fails.


## Meshtastic / OpenTAKServer gateway

In the Windows Builder **Advanced** mode, configure the Meshtastic gateway section when the deployment uses the infrastructure pattern:

- gateway type: `heltec_v3`
- role: `ots_mqtt`
- transport: `mqtt_tls`
- MQTT/TLS port: `8883`
- Root Topic: `opentakserver`

These fields are topology/diagnostic metadata. Do not paste MQTT passwords, tokens, certificates or private keys into the project or `.ftak`.

On Android, **Service / Diagnostics** reports the configured gateway and can test TCP reachability of the MQTT/TLS endpoint. A successful port check proves endpoint reachability only; it does not prove the Heltec V3 is physically connected to the LoRa mesh.

### Meshtastic live link status

Field TAK Hub now observes supported Meshtastic Android connection-state broadcasts. Connected/connecting/disconnected are reflected in the Hub UI. When no supported status is available, the state remains UNKNOWN rather than being inferred. The Hub does not bind to the removed AIDL service and does not take ownership of radio I/O.
