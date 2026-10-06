FIELD TAK HUB 2.3.0 — UI 2.4 BETA
INSTRUKCJA BUILDERA — PRZYGOTOWANIE PACZKI .FTAK
WERSJA POLSKA

0. INTERFEJS BUILDERA 2.4

Builder 2.3.0 zachowuje silnik i format paczki z linii 2.3, ale główny ekran korzysta z projektu UI 2.4 BETA: lewa nawigacja, centralny kreator, prawy panel statusu oraz kafelkowy wybór ATAK/Pluginy/Mapy/Overlays/Meshtastic/Server.

W prawym górnym rogu znajduje się szybki przełącznik języka PL / EN. Zmiana jest natychmiastowa i nie wymaga wejścia do ustawień.

Przycisk „Wybierz profil zalecany” ustawia wartości startowe profilu MILSIM. Przycisk „Konfiguruj” przy kafelku odsłania odpowiednią część trybu zaawansowanego.

1. CEL

Field TAK Hub Builder 2.3.0 służy do przygotowania podpisanej paczki .ftak dla użytkowników ATAK, OpenTAKServer i Meshtastic.

Najważniejsza zasada:

    ORGANIZATOR PRZYGOTOWUJE.
    UCZESTNIK SKANUJE.
    FIELD TAK HUB ROBI RESZTĘ.

Builder pracuje na katalogu source/. Katalog out/ jest katalogiem wynikowym. Plików wejściowych nie należy wkładać bezpośrednio do out/.

2. UTWORZENIE PROJEKTU

1. Uruchom Field TAK Hub Builder 2.3.0.
2. Wybierz New Project.
3. Podaj nazwę, np.:

    GGZS-TAK-Test-Meshtastic

4. Builder utworzy projekt w:

    Documents\FieldTAKHub\Projects\<NazwaProjektu>\

Przykładowa struktura:

    GGZS-TAK-Test-Meshtastic\
    |-- GGZS-TAK-Test-Meshtastic.fthproj
    |-- README.md
    |-- source\
    `-- out\

3. GDZIE WKŁADAĆ PLIKI

W katalogu source/ używamy:

    source\atak\
        ATAK Mission Package, pliki .pref i inne dane dla ATAK.

    source\plugins\
        pluginy ATAK w formacie .apk.

    source\maps\
        mapy i bazy map, np. MBTiles, GeoPackage, SQLite.

    source\overlays\
        nakładki KML/KMZ.

    source\config\
        dodatkowe pliki konfiguracyjne.

    source\data\
        pozostałe dane wdrożenia.

    source\meshtastic\
        konfiguracja Meshtastic.

    source\enrollment\
        opcjonalny URI enrollmentu ATAK/OTS.

4. UZUPEŁNIENIE DANYCH PACZKI

W Builderze ustaw:

Package ID:
    np. pl.ggzs.tak.test

Name:
    np. GGZS TAK Test

Version:
    np. 2.3.0-test.1

Publisher:
    nazwa autora lub organizacji

5. OPENTAKSERVER

Dla naszego środowiska testowego przykładowe wartości to:

Host:
    ggzstak.duckdns.org

CoT:
    8089

API / Enrollment:
    8446

Web:
    8443

W polu Host wpisz tylko nazwę domenową. Nie dodawaj https://.

6. TRYB BUILDERA

W wersji 2.3.0 dostępne są dwa tryby:

TRYB PROSTY
    Dla standardowej paczki bez administracyjnej konfiguracji.

TRYB ZAAWANSOWANY
    Dla administratora i testów. Umożliwia ręczne ustawienie Meshtastic, roli urządzenia, gatewaya MQTT/TLS, enrollmentu i zawartości source/.

Do testów Meshtastic wybierz TRYB ZAAWANSOWANY.

7. MESHTASTIC — PRZYGOTOWANIE KANAŁU

Potrzebujesz kanonicznego linku kanału Meshtastic, np.:

    https://meshtastic.org/e/...

W Builderze wklej go w polu Meshtastic Channel URL i wybierz Save.

Builder zapisze go jako:

    source\meshtastic\channel.url

UWAGA BEZPIECZEŃSTWA:
Link kanału Meshtastic może zawierać informacje umożliwiające dostęp do chronionego kanału. Traktuj go jako dane wrażliwe. Nie publikuj go w publicznym repozytorium ani w publicznej dokumentacji.

8. MESHTASTIC — USTAWIENIA PODSTAWOWE

Wybierz:

    Tryb Zaawansowany -> Meshtastic

Dla naszego testu bazowego ustaw:

Mode:
    HYBRID

Device Role:
    FIELD NODE

Device Model:
    rzeczywisty model urządzenia, np. tbeam_supreme

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

9. UNIWERSALNY SPRZĘT MESHTASTIC

Field TAK Hub jest sprzętowo neutralny. Device Model jest opisem sprzętu, a nie osobnym sterownikiem.

Przykładowe wartości:

    tbeam
    tbeam_supreme
    heltec_v3
    heltec_v4
    seeed_meshtracker_x1
    seeed_xiao_esp32s3
    seeed_xiao_nrf52840
    rak4631
    generic_meshtastic

Zmiana radia z Heltec na T-Beam albo SenseCAP nie wymaga zmiany architektury .ftak.

10. FIELD NODE

Dla zwykłego urządzenia terenowego ustaw:

    Device Role: FIELD NODE (Meshtastic TAK)
    Gateway Enabled: OFF

Przykład dla T-Beam Supreme:

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

11. GATEWAY MESHTASTIC <-> OPENTAKSERVER

Gateway jest elementem infrastruktury. Przykładowy układ:

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

Dla gatewaya ustaw:

    Device Role: GATEWAY

Przykładowe wartości:

    Gateway Enabled: ON
    Gateway Type: heltec_v3
    Gateway Role: ots_mqtt
    Gateway Transport: mqtt_tls
    MQTT Port: 8883
    Root Topic: opentakserver
    Secrets External: ON

Heltec V3 jest przykładem gatewaya, a nie wymaganiem projektu. Gatewayem może być także inne kompatybilne urządzenie Meshtastic.

12. SEKRETY — CZEGO NIE UMIESZCZAĆ W .FTAK

NIE wpisuj do projektu ani paczki:

    - haseł MQTT,
    - stałych tokenów produkcyjnych,
    - prywatnych kluczy TLS,
    - prywatnych kluczy Publishera,
    - innych trwałych danych uwierzytelniających.

Pole:

    Secrets External: ON

jest wymuszone przez walidację projektu.

13. ENROLLMENT

Opcjonalnie można przygotować:

    source\enrollment\enroll.url

Do wdrożeń testowych preferuj tokeny krótkotrwałe lub jednorazowe. Nie publikuj enrollment URI razem z publicznym repozytorium, jeśli zawiera sekret.

14. ANALYZE

Po skopiowaniu wszystkich plików wybierz Analyze.

Sprawdź, czy Builder wykrywa:

    - ATAK Mission Package,
    - pluginy APK,
    - mapy,
    - overlay,
    - dane,
    - konfigurację Meshtastic.

W przypadku Meshtastic musi być poprawnie przygotowany:

    source\meshtastic\channel.url

15. BUILD PACKAGE

Wybierz Build Package.

Przed zatwierdzeniem sprawdź:

    - nazwę paczki,
    - wersję,
    - serwer OTS,
    - CoT 8089,
    - API/Enrollment 8446,
    - Web 8443,
    - Package validity,
    - QR validity,
    - liczbę plików.

Po zatwierdzeniu wynik będzie zapisany jako:

    out\<nazwa-paczki>.ftak

16. CO TRAFI DO .FTAK

W uproszczeniu:

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

profile.json jest generowany przez Buildera z ustawień profilu.

17. PACKAGE VALIDITY I QR VALIDITY

Package validity określa ważność samej paczki .ftak.

QR validity określa ważność adresu/QR używanego do dystrybucji paczki.

Przykład:

    Package validity: 6 h
    QR validity: 6 h
    Max downloads: 50

18. KONTROLA PRZED WYSŁANIEM TESTEROM

    [ ] .ftak znajduje się w out/
    [ ] właściwa wersja
    [ ] właściwy Host OTS
    [ ] CoT = 8089
    [ ] API/Enrollment = 8446
    [ ] Web = 8443
    [ ] właściwy Channel URL
    [ ] właściwy Device Role
    [ ] właściwy Device Model
    [ ] Gateway OFF dla Field Node
    [ ] Gateway ON tylko dla gatewaya
    [ ] brak MQTT credentials
    [ ] brak prywatnych kluczy
    [ ] Channel URL traktowany jako dane wrażliwe

19. ZALECANY PIERWSZY TEST 2.3.0

Dla pierwszego wdrożenia testowego zalecamy:

    Gateway:
        Heltec V3

    Field Nodes:
        T-Beam Supreme
        SenseCAP MeshTracker X1

Następnie sprawdź cały łańcuch:

    LoRa -> Gateway -> MQTT/TLS -> OpenTAKServer -> ATAK

20. ZASADA PROJEKTU

Field TAK Hub 2.3.0 ma pozostać uniwersalnym narzędziem Meshtastic. Heltec V3 jest przykładowym gatewayem infrastrukturalnym, nie ograniczeniem projektu.

Koniec instrukcji.


## DOŁĄCZANIE ATAK I MESHTASTIC APK

```text
source\atak\<ATAK-CIV>.apk
source\apps\meshtastic.apk
source\plugins\*.apk
```

Builder zapisuje je jako `payload/atak/atak.apk`, `payload/apps/meshtastic.apk` i `payload/plugins/*.apk`. Field TAK Hub na Androidzie uruchamia instalację/aktualizację ATAK, Meshtastic i pluginów; system Android wymaga zgody użytkownika.
