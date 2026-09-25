# Meshtastic HYBRID — Field TAK Hub 2.3

## Tryby

### HYBRID
Hub preferuje dostępny Direct path, a w przeciwnym razie korzysta z Compatible path.

### COMPATIBLE
`ATAK -> official Meshtastic ATAK Plugin -> Meshtastic Android -> BLE/USB -> radio`.

### DIRECT
`ATAK -> standalone Meshtastic ATAK Plugin -> BLE/USB -> radio`.

Field TAK Hub nie przejmuje własności połączenia BLE z radiem. Dzięki temu unikamy konfliktów dwóch klientów BLE.

## Pakiet

```text
payload/meshtastic/channel.url
payload/meshtastic/profile.json
```

Przykład profilu:

```json
{
  "version": 2,
  "mode": "hybrid",
  "channelName": "GGZS-TAK",
  "region": "EU_868",
  "modemPreset": "SHORT_FAST",
  "hopLimit": 3,
  "pli": true,
  "geoChat": true,
  "otsRelay": true,
  "fileTransfer": false,
  "gateway": {
    "enabled": true,
    "type": "generic_meshtastic",
    "role": "ots_mqtt",
    "transport": "mqtt_tls",
    "mqttPort": 8883,
    "rootTopic": "opentakserver",
    "secretsExternal": true
  }
}
```

## Bezpieczeństwo

Nie umieszczać w profilu:
- haseł MQTT;
- tokenów MQTT;
- prywatnych kluczy;
- prywatnego klucza Publishera;
- danych logowania OTS.

## Gateway

Gateway Meshtastic jest traktowany jako osobny element infrastruktury. Field TAK Hub nie wymaga konkretnego producenta ani modelu gatewaya; `meshtastic.gateway.type` jest polem opisowym i może zawierać np. `heltec_v3`, `tbeam_supreme`, `seeed_meshtracker_x1` albo własny identyfikator urządzenia.


## Topologia 2.3

Field TAK Hub nie staje się klientem BLE gatewaya. Topologia infrastruktury jest jawnie opisana w manifeście/pakiecie jako: `Meshtastic field node → LoRa → Heltec V3 gateway → MQTT/TLS → OpenTAKServer`.

Pole `meshtastic.gateway` opisuje wyłącznie bezpieczne dane topologii: typ/model urządzenia, rolę, transport, port MQTT i Root Topic. Hasła MQTT, tokeny, certyfikaty i klucze pozostają poza `.ftak`.

Android wykorzystuje te dane do diagnostyki konfiguracji. Sprawdzenie portu MQTT potwierdza tylko dostępność TCP/TLS endpointu; nie jest dowodem, że fizyczny Heltec V3 jest połączony z LoRa. BLE/radio pozostaje pod kontrolą Meshtastic/ATAK.


## Uniwersalność sprzętowa

Field TAK Hub działa na poziomie profilu Meshtastic i nie wymaga implementowania osobnego sterownika dla każdego radia. Warstwa BLE/USB/radio pozostaje po stronie aplikacji Meshtastic i/lub oficjalnego pluginu ATAK, natomiast `.ftak` opisuje konfigurację sieci i topologię.

Przykładowe urządzenia spotykane w ekosystemie Meshtastic obejmują LILYGO T-Beam/T-Beam Supreme, Heltec V3/V4, Seeed MeshTracker X1, Seeed XIAO oraz inne urządzenia wspierane przez Meshtastic. Dostępność GPS, BLE, USB, Wi-Fi, baterii, ekranu i funkcji repeater/gateway zależy od konkretnego modelu i nie jest zakładana przez Field TAK Hub.

W praktyce oznacza to:
- **field node**: urządzenie z Meshtastic używane przez operatora w terenie;
- **gateway**: wybrane urządzenie/host z dostępem do IP/MQTT, przekazujące ruch mesh do OpenTAKServer;
- **Field TAK Hub**: narzędzie uniwersalne do konfiguracji, dystrybucji i diagnostyki, a nie sterownik konkretnego radia.
