# Field TAK Hub 2.3.0 — PL

**Organizator przygotowuje. Uczestnik skanuje. Field TAK Hub robi resztę.**

Field TAK Hub przygotowuje telefon do pracy z ATAK, OpenTAKServer i Meshtastic. Wersja 2.3.0 skupia się na prostym wdrożeniu uczestnika, pełnej obsłudze administracyjnej oraz diagnostyce.

## Najważniejsze funkcje

- import podpisanych paczek `.ftak`;
- provisioning telefonu z QR;
- enrollment użytkownika z QR;
- import Data Package z QR;
- Simple Mode i Advanced Mode;
- Smart Diagnosis i Recovery;
- Meshtastic HYBRID i przekazanie przygotowanej konfiguracji;
- diagnostyka połączenia telefonu z radiem;
- obsługa różnych urządzeń Meshtastic;
- interfejs Polski / English;
- wsparcie autora przez BuyCoffee;
- CI GitHub Actions.

## Dokumentacja

- [Instrukcja użytkownika](docs/README_USER_PL.txt)
- [Instrukcja Buildera](docs/README_BUILDER_PL.txt)
- [Instrukcja testów 2.3.0](docs/TESTING_2_3_0_PL.md)
- [Architektura](docs/ARCHITECTURE.md)
- [Meshtastic HYBRID](docs/MESHTASTIC_HYBRID.md)
- [Format `.ftak`](docs/FTAK_FORMAT.md)
- [Model bezpieczeństwa](docs/SECURITY_MODEL.md)

## Architektura Meshtastic

```text
Telefon -> Radio Meshtastic -> LoRa -> Gateway -> MQTT/TLS -> OpenTAKServer -> ATAK
```

Field TAK Hub przekazuje konfigurację, ale nie zastępuje warstwy BLE/USB i sterownika radiowego Meshtastic.

## Ważne

Nie publikuj haseł, kluczy, tokenów, prywatnych Channel URL ani sekretów enrollmentu.


## Instalacja ATAK i Meshtastic

Field TAK Hub 2.3.0 może instalować/aktualizować aplikacje wymagane przez paczkę `.ftak` — ATAK, aplikację Meshtastic oraz pluginy.

```text
payload/atak/atak.apk
payload/apps/meshtastic.apk
payload/plugins/*.apk
payload/meshtastic/channel.url
payload/meshtastic/profile.json
```

Podczas **PRZYGOTUJ TELEFON** Hub kolejno sprawdza ATAK, instaluje/aktualizuje dołączoną aplikację Meshtastic, a następnie pluginy. Android wymaga potwierdzenia instalacji APK przez użytkownika; cicha instalacja nie jest wykonywana.

Builder używa `source/atak/*.apk` dla ATAK oraz `source/apps/meshtastic.apk` dla aplikacji Meshtastic.
