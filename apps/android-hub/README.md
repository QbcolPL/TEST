# Field TAK Hub — aplikacja Android (od zera)

Nowa aplikacja telefonu gracza. Zastępuje makietę z `apps/mobile`.

## Co robi

| Ekran | Funkcje |
|---|---|
| Onboarding (pierwsze uruchomienie, potem ŚCIĄGA → Uprawnienia) | 5 uprawnień z wyjaśnieniem, do czego służy każde; aktywny profil |
| SKANER | QR z aparatu, plik `.ftak`, obraz z kodem, wklejony link; status ATAK / Meshtastic / serwera OTS; callsign z edytowalnym skrótem Meshtastic; „Wstrzyknij do ATAK + Meshtastic” |
| PACZKI | menedżer paczek: lista, aktywacja, szczegóły, usuwanie, udostępnianie przez hotspot Wi-Fi, przez tę samą sieć albo plikiem |
| OPORD | aktywny dopiero po imporcie rozkazu; SOS MGRS (SMS do organizatora), zakładki dni, tabela częstotliwości, mapa z Załącznika 3 |
| ŚCIĄGA | numer organizatora, częstotliwości, hasła i kryptonimy, moje ustawienia |

## Zgodność z Builderem

Aplikacja czyta wszystko, co dziś produkuje Builder:

- **FTAK v2 podpisana** — `META-INF/fieldtak.json`, sumy SHA-256 i podpis Ed25519 (weryfikowane; inny klucz wydawcy niż poprzednio wymaga potwierdzenia),
- **FTAK v3.0 frakcja** — `config.json`/`manifest.json`, `opord.json`, `opord.html`, pliki z `source/`,
- **FTAK v2.4 frakcja** — `manifest.json` + `source/tactical_config.json`,
- kody QR: `fieldtak://provision?url=` (LAN), `fieldtak://provision?packageUrl=&sha256=` (chmura, Dysk Google), `http://IP:8766/deploy/…ftak`, `http://IP:8766/opord/…html`, „QR offline (JSON)”, kody z plakatów v2.4, linki `tak://…/enroll` i kanały `meshtastic.org/e/#`.

Logika formatów jest w `app/src/main/java/com/fieldtakhub/hub/core` (czysty Kotlin, bez Androida) i ma testy w `app/src/test`.

## Budowanie

`FTH_BUILD_ALL_IN_ONE.bat` w katalogu głównym repozytorium sam wybiera ten projekt, gdy istnieje `apps/android-hub/settings.gradle.kts`.
Lokalnie: `./gradlew assembleDebug testDebugUnitTest` (JDK 17, Android SDK 36).

Wersje: Gradle 8.14.3 (wrapper), Android Gradle Plugin 8.13.2, Kotlin 2.2.21, Compose BOM 2025.12.00, minSdk 26, targetSdk 35.

## Ograniczenia systemowe

- Każdą instalację APK i każdą zmianę ustawień ATAK / Meshtastic użytkownik potwierdza w oknie tej aplikacji — Android nie pozwala inaczej (poza telefonami zarządzanymi przez MDM).
- Meshtastic od 2.8 nie udostępnia innym aplikacjom zmiany nazwy radia — długą i krótką nazwę ustawia się raz ręcznie (aplikacja kopiuje skrót do schowka i otwiera Meshtastic).
- Hotspot do dzielenia się paczką tworzy system (losowa nazwa i hasło). Odbiór automatyczny działa od Androida 10; na starszych telefon łączy się ręcznie.
