# Field TAK Hub 2.3.0

> **Organizator przygotowuje. Uczestnik skanuje. Field TAK Hub robi resztę.**

Field TAK Hub to lekkie narzędzie do przygotowania i diagnostyki telefonu używanego z **ATAK, OpenTAKServer i Meshtastic**. Projekt łączy przygotowanie telefonu, import podpisanych paczek `.ftak`, obsługę kodów QR, diagnostykę oraz przekazanie konfiguracji Meshtastic w jeden prosty workflow.

## Co zawiera projekt

- **Field TAK Hub Android 2.3.0** — aplikacja dla uczestnika i administratora.
- **Field TAK Hub Builder 2.3.0** — Windows Builder do przygotowywania paczek `.ftak`.
- **`.ftak` schema v2** — podpisane paczki wdrożeniowe.
- **Simple Mode / Advanced Mode** — prosty interfejs dla uczestnika i pełny interfejs dla administratora.
- **QR workflows** — provisioning telefonu, enrollment użytkownika oraz import Data Package.
- **Meshtastic HYBRID** — konfiguracja kanału/profilu i diagnostyka bez zastępowania oficjalnej warstwy radiowej Meshtastic.
- **Smart Diagnosis / Recovery** — sprawdzenie stanu telefonu i podstawowa naprawa procesu wdrożenia.
- **Dwujęzyczny interfejs** — Polski / English.
- **GitHub Actions CI** — kontrola źródeł, testy kontraktowe, Android i Windows Builder.

## Najważniejsza zasada architektury

Field TAK Hub **nie jest sterownikiem konkretnego radia**. Warstwa BLE/USB i rzeczywista komunikacja z radiem pozostają po stronie Meshtastic i odpowiedniego pluginu ATAK/Meshtastic. Dzięki temu projekt nie jest związany wyłącznie z jednym urządzeniem.

Przykładowa topologia:

```text
Telefon
  ├── ATAK
  ├── Meshtastic / plugin
  │      |
  │      v
  │    Radio Meshtastic
  │      |
  │     LoRa
  │      v
  │   Gateway (np. Heltec V3)
  │      |
  │   MQTT/TLS
  v      v
OpenTAKServer
  |
 CoT
  v
ATAK
```

Architektura obsługuje m.in. T-Beam, T-Beam Supreme, Heltec, SenseCAP MeshTracker X1, Seeed/XIAO, RAK i inne kompatybilne urządzenia Meshtastic.

## Simple Mode

Simple Mode jest przeznaczony dla użytkownika terenowego. Pokazuje przede wszystkim:

- przygotowanie telefonu,
- import `.ftak`,
- kontrolę stanu,
- konfigurację radia, gdy paczka zawiera właściwą konfigurację,
- automatyczne przejście do trybu zaawansowanego tylko wtedy, gdy jest potrzebne.

## Advanced Mode

Advanced Mode jest przeznaczony dla administratora i testera. Zawiera osobne sekcje dla:

- przygotowania telefonu,
- OpenTAKServer — **Enrollment** i **Data Packages**, 
- Meshtastic,
- diagnostyki i historii.

## Nawigacja Android

Wewnątrz aplikacji przycisk systemowy **Wstecz** wraca do poprzedniego ekranu. Na ekranie głównym pojedyncze naciśnięcie nie zamyka aplikacji; drugie szybkie naciśnięcie w ciągu 2 sekund przenosi ją do tła.

## Wsparcie projektu

W aplikacji dostępny jest nienachalny panel wsparcia autora z kodem QR i linkiem do BuyCoffee:

`https://buycoffee.to/qbcol`

## Dokumentacja

### Użytkownik

- [PL — instrukcja użytkownika](docs/README_USER_PL.txt)
- [EN — user guide](docs/README_USER_EN.txt)
- [PL — instrukcja testów 2.3.0](docs/TESTING_2_3_0_PL.md)
- [EN — 2.3.0 test guide](docs/TESTING_2_3_0_EN.md)

### Builder

Builder 2.3.0 używa interfejsu **UI 2.4 Preview** oraz szybkiego przełącznika języka **PL / EN** w prawym górnym rogu. Szczegóły: `apps/builder/README.md`.

- [PL — instrukcja Buildera](docs/README_BUILDER_PL.txt)
- [EN — Builder guide](docs/README_BUILDER_EN.txt)
- [PL — przygotowanie paczki Meshtastic](docs/MESHTASTIC_HARDWARE_2_3_0_PL.md)

### Techniczna

- [Architektura](docs/ARCHITECTURE.md)
- [Format `.ftak`](docs/FTAK_FORMAT.md)
- [Meshtastic](docs/MESHTASTIC.md)
- [Meshtastic HYBRID](docs/MESHTASTIC_HYBRID.md)
- [Model bezpieczeństwa](docs/SECURITY_MODEL.md)
- [Build](docs/BUILD.md)
- [Build i podpisywanie](docs/BUILD_AND_SIGN.md)
- [Release 2.3.0](docs/RELEASE_2_3_0.md)

## Struktura repozytorium

```text
apps/
  android/                  Android Hub
  builder/                  Windows Builder + testy

.github/workflows/          CI/CD

docs/                       Dokumentacja PL/EN

examples/                   Minimalny przykład projektu Buildera

schemas/                    Schematy JSON

scripts/                    Skrypty pomocnicze

tests/                      Kontrakty i bramy źródłowe

tools/                      Instrukcje i helpery builda
```

Repozytorium **nie zawiera gotowych APK/EXE ani prywatnych danych wdrożeniowych**. Artefakty są tworzone przez GitHub Actions.

## Bezpieczeństwo

Nie publikuj w repozytorium:

- haseł MQTT,
- prywatnych kluczy TLS,
- prywatnych kluczy Publishera,
- stałych tokenów produkcyjnych,
- prywatnych Channel URL Meshtastic,
- enrollment URI zawierających sekrety.

## Licencja

Sprawdź plik [LICENSE](LICENSE).

## Autor

**Jakub /Qbcol/ Baron**

## Języki

- [README PL](README_PL.md)
- [README EN](README_EN.md)
