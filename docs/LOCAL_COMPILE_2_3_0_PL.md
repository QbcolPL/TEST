# Field TAK Hub 2.3.0 — pakiet do lokalnej kompilacji

## Zakres tej paczki

Ta paczka jest bazą **Field TAK Hub 2.3.0** do lokalnej kompilacji Androida oraz Windows Buildera.

### Android — zawarte funkcje

- Simple Mode i Advanced Mode
- opcjonalne kroki z przyciskiem **POMIŃ**
- pomijanie Enrollment, Data Package oraz Meshtastic tam, gdzie krok jest opcjonalny
- zapamiętanie pominięcia i poprawne przejście do kolejnego kroku
- Smart Diagnosis / Recovery
- import `.ftak`
- QR provisioning / enrollment / data package
- OpenTAKServer
- Meshtastic / LoRa / HYBRID
- obsługa Field Node / Gateway w modelu konfiguracji
- diagnostyka gatewaya MQTT/TLS
- poprawiona nawigacja Android Back: pierwszy Back wraca, drugi Back w ciągu 2 s minimalizuje aplikację
- język PL/EN

### Builder 2.3 — baza UI pod kierunek 2.4

Builder pozostaje wersją **2.3.0**, ale jego układ i podział funkcji są przygotowane jako baza dla UI 2.4:

- Simple / Advanced
- profile Meshtastic
- Field Node / Gateway
- Meshtastic ↔ OpenTAKServer Gateway
- MQTT/TLS
- `SecretsExternal` — sekrety poza `.ftak`
- Enrollment
- Package validity / QR validity / limit pobrań
- LAN i zewnętrzna dystrybucja HTTPS/Cloud
- analiza zawartości i podgląd builda
- import legacy 1.x
- PL/EN

Nie zmieniamy numeru Buildera na 2.4 — **2.4 jest kierunkiem UI**, natomiast kompatybilność pakietu pozostaje 2.3.0.

## Android Studio

Otwórz:

`apps/android`

Wymagania:

- Android Studio
- JDK 17+
- Android SDK zgodny z `compileSdk` projektu
- dostęp do internetu przy pierwszej synchronizacji Gradle, aby pobrać zależności

Następnie:

1. Open → `apps/android`
2. poczekaj na Gradle Sync
3. Build → Make Project
4. Build → Build APK(s) → Build APK(s)

Dla debug APK użyj zadania:

`assembleDebug`

## Windows Builder

Otwórz repozytorium w katalogu głównym i uruchom:

`tools\BUILD_WINDOWS_BUILDER_LOCAL_v3.cmd`

Wymagany jest .NET SDK 10.x x64.

## Kontrola źródeł

Przed kompilacją można uruchomić:

`python tests\full_static_gate.py`

Oczekiwany wynik końcowy:

`FULL STATIC GATE: PASS`

## Ważne

Paczka nie zawiera prywatnych kluczy podpisujących, haseł MQTT ani innych sekretów.

`keystore.properties` należy utworzyć lokalnie na komputerze deweloperskim.
