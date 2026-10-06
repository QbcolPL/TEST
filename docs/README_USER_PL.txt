FIELD TAK HUB 2.3.0
INSTRUKCJA UŻYTKOWNIKA — ANDROID
WERSJA POLSKA

1. DO CZEGO SŁUŻY FIELD TAK HUB?

Field TAK Hub przygotowuje telefon do pracy z ATAK, OpenTAKServer i Meshtastic.

Aplikacja została zaprojektowana tak, aby użytkownik nie musiał ręcznie przepisywać wielu ustawień.

Zasada działania:

    ORGANIZATOR PRZYGOTOWUJE.
    UCZESTNIK SKANUJE.
    FIELD TAK HUB ROBI RESZTĘ.

Field TAK Hub nie przejmuje bezpośrednio BLE/USB ani radiowej komunikacji Meshtastic. Warstwę radiową obsługuje aplikacja Meshtastic oraz odpowiedni plugin.

2. CZEGO POTRZEBUJESZ?

W zależności od wdrożenia potrzebujesz:

    - telefonu z Androidem,
    - Field TAK Hub,
    - ATAK CIV,
    - aplikacji Meshtastic,
    - pliku .ftak lub odpowiedniego kodu QR otrzymanego od organizatora.

3. PIERWSZE URUCHOMIENIE

Uruchom Field TAK Hub.

W wersji 2.3.0 dostępne są dwa tryby:

TRYB PROSTY
    Domyślny tryb dla użytkownika terenowego.

TRYB ZAAWANSOWANY
    Dla administratora, testera i diagnostyki.

Zwykły użytkownik powinien pozostać w TRYBIE PROSTYM.

4. QR — PRZYGOTOWANIE TELEFONU

Na ekranie głównym użyj funkcji skanowania przygotowania telefonu.

Zeskanuj kod otrzymany od organizatora.

Field TAK Hub rozpocznie odpowiedni proces zgodnie z przygotowanym wdrożeniem.

5. QR — ENROLLMENT

Jeżeli organizator przekazał QR enrollmentu:

    Skanuj enrollment

Następnie skieruj aparat na kod.

Enrollment może być krótkotrwały lub jednorazowy. Nie publikuj otrzymanego URI.

6. QR — DATA PACKAGE

Jeżeli organizator przekazał QR do Data Package:

    Skanuj Data Package

Zeskanuj kod i poczekaj na zakończenie importu do ATAK.

Aplikacja rozpoznaje różne cele QR i nie powinna uruchamiać innego workflow po zeskanowaniu niewłaściwego typu kodu.

7. IMPORT PLIKU .FTAK

Jeżeli otrzymasz plik:

    nazwa-paczki.ftak

wybierz:

    Importuj paczkę

następnie wskaż plik .ftak.

Nie edytuj ręcznie zawartości pliku .ftak.

8. CO MOŻE ZAWIERAĆ PACZKA?

W zależności od wdrożenia paczka może zawierać:

    - konfigurację ATAK,
    - pluginy,
    - mapy,
    - overlay,
    - dane,
    - konfigurację Meshtastic,
    - informacje o OpenTAKServer,
    - dane potrzebne do enrollmentu.

Dla Meshtastic paczka może zawierać:

    payload/meshtastic/channel.url
    payload/meshtastic/profile.json

9. MESHTASTIC — CO MA ZROBIĆ UŻYTKOWNIK?

Jeżeli administrator przygotował poprawną paczkę, nie trzeba ręcznie przepisywać parametrów kanału.

Field TAK Hub przekazuje przygotowaną konfigurację. Następnie użytkownik korzysta z aplikacji Meshtastic, pluginu ATAK i swojego radia.

Jeżeli Meshtastic jest opcjonalny w danym wdrożeniu, w TRYBIE PROSTYM można użyć przycisku **POMIŃ MESHTASTIC** albo **POMIŃ OPCJONALNY KROK**. Pominięcie nie oznacza błędu — konfigurację można wykonać później w TRYBIE ZAAWANSOWANYM.

10. PRZYKŁADOWY TOR KOMUNIKACJI

    Telefon
      |
      +-- ATAK
      +-- Meshtastic
      +-- plugin Meshtastic
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

Przykładowy gateway infrastrukturalny to Heltec V3.

11. FIELD NODE A GATEWAY

Typowy użytkownik terenowy pracuje jako:

    FIELD NODE

Przykłady:

    - T-Beam,
    - T-Beam Supreme,
    - SenseCAP MeshTracker X1,
    - inne kompatybilne radia Meshtastic.

Gateway jest elementem infrastruktury. Użytkownik nie powinien sam zmieniać swojego urządzenia na Gateway bez instrukcji administratora.

12. CZY FIELD TAK HUB JEST TYLKO DLA HELTECA?

Nie.

Field TAK Hub 2.3.0 jest projektowany jako narzędzie uniwersalne dla sprzętu Meshtastic.

Przykładowo może współpracować w ramach tej samej architektury z:

    T-Beam
    T-Beam Supreme
    Heltec
    SenseCAP MeshTracker X1
    Seeed/XIAO
    RAK
    innymi kompatybilnymi urządzeniami Meshtastic

13. CO OZNACZA STATUS MESHTASTIC?

Status aplikacji Meshtastic może informować o:

    - obecności aplikacji,
    - dostępności konfiguracji,
    - stanie wymagającym sprawdzenia,
    - problemie.

Sama informacja, że Meshtastic jest zainstalowany, NIE potwierdza jeszcze, że radio jest połączone i że działa pełna droga radiowa.

14. „TESTUJ MESHTASTIC” / SPRAWDŹ MESHTASTIC

Przycisk „Sprawdź aplikację Meshtastic” ma charakter informacyjny. Sprawdza, czy aplikacja Meshtastic jest zainstalowana i czy paczka zawiera konfigurację kanału. Nie otwiera automatycznie aplikacji i nie potwierdza połączenia BLE/radio. Do uruchomienia aplikacji służy osobny przycisk „Otwórz Meshtastic”.

Nie traktuj jej jako gwarancji pełnego testu:

    telefon -> radio -> LoRa -> gateway -> MQTT -> OTS -> ATAK

Do takiego testu potrzebne jest rzeczywiste środowisko radiowe.

15. TRYB ZAAWANSOWANY

Tryb zaawansowany jest przeznaczony dla:

    - administratora,
    - testera,
    - osoby prowadzącej diagnostykę,
    - osoby odpowiedzialnej za wdrożenie.

Można tam sprawdzać m.in. konfigurację, OpenTAKServer, Meshtastic i gateway.

16. DIAGNOSTYKA OPENTAKSERVER

Jeżeli administrator poprosi o diagnostykę:

    Tryb zaawansowany -> Diagnostyka / Service

W naszym środowisku testowym przykładowy Host to:

    ggzstak.duckdns.org

Wykrycie hosta lub dostępność portu nie oznacza jeszcze, że cały system end-to-end działa poprawnie.

17. DIAGNOSTYKA GATEWAYA

Diagnostyka może sprawdzić osiągalność endpointu MQTT/TLS.

Udany test TCP portu oznacza tylko osiągalność endpointu. Nie oznacza, że Heltec lub inne radio gateway jest fizycznie połączone z siecią LoRa.

18. NAJCZĘSTSZA CHECKLISTA

    [ ] Field TAK Hub jest zainstalowany
    [ ] ATAK jest zainstalowany
    [ ] Meshtastic jest zainstalowany, jeśli jest wymagany
    [ ] otrzymałem właściwy .ftak lub QR
    [ ] paczka nie jest przeterminowana
    [ ] mam wymagany dostęp do sieci
    [ ] radio jest włączone
    [ ] radio jest połączone z telefonem

19. CO ZROBIĆ, JEŻELI COŚ NIE DZIAŁA?

1. Nie zmieniaj samodzielnie ustawień gatewaya ani serwera.
2. Sprawdź, czy masz właściwy .ftak/QR.
3. Sprawdź, czy Meshtastic i ATAK są uruchomione/zainstalowane.
4. Sprawdź połączenie telefonu z radiem.
5. Uruchom diagnostykę w trybie zaawansowanym.
6. Przekaż administratorowi wynik diagnostyki.

20. CZEGO NIE ROBIĆ

Nie wpisuj ani nie publikuj:

    - haseł MQTT,
    - prywatnych kluczy,
    - stałych tokenów,
    - enrollment URI zawierających sekrety,
    - prywatnych Channel URL.

Nie edytuj ręcznie pliku .ftak.

21. NAJPROSTSZA INSTRUKCJA DLA UCZESTNIKA

    1. Zainstaluj Field TAK Hub.
    2. Jeżeli paczka zawiera ATAK i Meshtastic APK, Field TAK Hub przeprowadzi ich instalację/aktualizację podczas „Przygotuj telefon”. Android wymaga potwierdzenia instalacji przez użytkownika.
    3. Otrzymasz QR lub plik .ftak.
    4. Zeskanuj QR lub zaimportuj .ftak.
    5. Połącz radio Meshtastic z telefonem, jeśli jest wymagane.
    6. Uruchom ATAK.
    7. Sprawdź status.
    8. Gotowe — możesz ruszyć w teren.

22. TEST 2.3.0

Dla pierwszego testu zalecamy sprawdzenie:

    - importu .ftak,
    - wszystkich trzech typów QR,
    - wykrycia Meshtastic,
    - połączenia telefonu z radiem,
    - PLI,
    - GeoChat,
    - LoRa -> gateway,
    - gateway -> MQTT/TLS,
    - MQTT -> OpenTAKServer,
    - OTS -> ATAK.

Koniec instrukcji.


MESHTASTIC — rzeczywisty stan połączenia
Field TAK Hub nasłuchuje opcjonalnego mostu statusu aplikacji Meshtastic. Jeżeli aplikacja zgłosi połączenie, Hub pokaże „Radio Meshtastic jest połączone”. Jeżeli aplikacja nie udostępni stanu, Hub pokazuje „Niezweryfikowane” zamiast zgadywać. Hub nie przejmuje BLE/USB ani sterowania radiem.

DODATEK 2.3 — PROSTE PRZYGOTOWANIE RADIA
-----------------------------------------
W trybie prostym użyj przycisku „SKONFIGURUJ RADIO”, jeśli paczka zawiera konfigurację Meshtastic.
Field TAK Hub otwiera przygotowaną konfigurację kanału w aplikacji Meshtastic lub przekazuje użytkownika do ATAK, gdy wykryto bezpośredni plugin Meshtastic.

Hub nie implementuje własnego sterownika BLE/USB. Zapis ustawień radia pozostaje po stronie aplikacji/pluginu Meshtastic. Po powrocie uruchom „SPRAWDŹ WSZYSTKO”, aby potwierdzić stan radia.


INSTALLACJA APLIKACJI
Paczka `.ftak` może zawierać ATAK (`payload/atak/atak.apk`), aplikację Meshtastic (`payload/apps/meshtastic.apk`) oraz pluginy (`payload/plugins/*.apk`). „PRZYGOTUJ TELEFON” sprawdza i uruchamia instalację/aktualizację tych komponentów w kolejności. Android wymaga potwierdzenia instalacji. Jeżeli APK nie ma w paczce, Hub nie pobiera go samodzielnie.


23. POMIJANIE OPCJONALNYCH CZYNNOŚCI

W TRYBIE PROSTYM Field TAK Hub pokazuje przyciski pomijania tylko dla czynności opcjonalnych. Możesz pominąć:

    - Meshtastic,
    - enrollment,
    - import Data Package,
    - konfigurację radia Meshtastic.

Pominięcie jest zapisywane jako `SKIPPED`. Nie jest błędem. Później możesz wykonać pominiętą czynność w TRYBIE ZAAWANSOWANYM.

Nie można pomijać czynności wymaganych do podstawowego przygotowania telefonu, takich jak integralność paczki, zaufanie wydawcy, instalacja wymaganego ATAK lub wymaganych pluginów.
