# Generator paczek .ftak: paczka zgodna z ekranem, bez atrap (etap 1 porządkowania modułu 1)

Moduł 1 Buildera (generator paczek) ma być zrozumiały dla osoby, która pierwszy raz organizuje grę.
Etap 1 usuwa to, co wprowadzało w błąd: pola bez wpływu na telefon, przykładowe dane udające prawdziwe
i budowanie paczek z innego stanu niż ten na ekranie. Układ ekranu zostaje — przebudowa okna przyjdzie w kolejnych etapach.

## Paczka powstaje z tego, co widać na ekranie

Budowanie czytało plik projektu z dysku. Bez zapisu był to szablon nowego projektu („NOWA OPERACJA”,
domyślne strony, pusty klucz), a z zapisanym projektem — jego ostatnia zapisana wersja. Zmiana nazwy strony
bez Ctrl+S dawała paczkę ze starą nazwą.

Teraz „⚡ Zbuduj paczkę strony”, „⚡ Buduj Wszystkie Strony” i „📡 Dystrybucja LAN/WIFI Hotspot” biorą bieżący stan okna
(ten sam, który zapisuje Ctrl+S i autozapis). Zapis projektu nie jest do tego potrzebny.

## Karta strony konfliktu

Karta ma pięć bloków, od góry:

1. **Nazwa, kolor, nakładki** — przycisk folderu `factions\<strona>` na pliki .kml/.kmz/.gpx tylko tej strony.
2. **ATAK — serwer TAK i wysyłanie pozycji** — adres serwera (`tak.example.org:8089`; bez portu telefon
   łączy się na 8089), link rejestracji OpenTAK (`tak://com.atakmap.app/enroll?…`) i wysyłanie pozycji.
   Wcześniej adresu serwera nie było na ekranie, a link rejestracji siedział w osobnej sekcji z przyciskami Save/Clear.
3. **Kanały głosowe i telefon SOS** — tabela kanałów (na plakat, do rozkazu i do telefonu) i numer alarmowy.
4. **Kanał Meshtastic** — region, preset, hop limit, nazwa kanału, własny link kanału, klucz PSK z przyciskiem
   „🎲 Losuj klucz” i programator płytek USB tej strony.
5. **Plakat A4** — co ma się znaleźć na plakacie; na dole: folder paczki, plakat + QR, budowanie paczki tej strony.

### Usunięte pola (nie trafiały do telefonu)

Paczka v3.0 niesie tylko część ustawień — resztę telefon i tak pomijał. Z karty zniknęło około 25 elementów:

- interwały pozycji (Const Rel/LTE, Const Mesh, Dynamic Max/Min, postój LTE/Mesh, TCP Timeout),
  „Wysyłaj pozycję CoT na zewnątrz”, „Połączenia bezstrumieniowe Mesh”, ważność w karcie strony;
- w Meshtastic: tryb, rola urządzenia, model, przełączniki PLI / GeoChat / OTS Relay / File Transfer;
- sekcja „Gateway Meshtastic ↔ OTS” (Gateway enabled, Secrets external);
- przyciski Save / Open / Copy / Clear pliku konfiguracji strony.

**Wysyłanie pozycji** ma dwie pozycje — te, które zna telefon:
„Stałe — co 5 s (przez mesh co 15 s)” albo „Dynamiczne — od 2 do 20 s, zależnie od ruchu”.

**Region LoRa**: EU_868, EU_433, UA_868, UA_433, US, ANZ. Dawne „US_915” i „ANZ_915” nie istniały w Meshtastic —
koder linku kanału i telefon zamieniały je na EU_868. Projekt z taką wartością pokazuje teraz US albo ANZ.
Programator USB używa tej samej tabeli regionów.

### Sprawdzanie pól

Pod polem pojawia się uwaga, gdy telefon nie przyjmie wartości albo zrobi z nią coś innego, niż sugeruje ekran:

| Pole | Uwaga, gdy |
|---|---|
| Adres serwera | jest `https://`, ścieżka, spacja, IPv6 albo zły port |
| Link rejestracji | to nie link `tak://com.atakmap.app/enroll`, albo brakuje w nim `host`, `username` lub `token` |
| Nazwa kanału | ma ponad 11 bajtów (polska litera to 2) — uwaga mówi, jaką nazwę dostaną urządzenia |
| Własny link kanału | to nie link `https://meshtastic.org/e/#…` |
| Klucz PSK | jest pusty lub uszkodzony (paczka dostałaby publiczny klucz Meshtastic), to klucz publiczny albo ma złą długość; dobry klucz: „✓ klucz prywatny (AES-256)” |

Własny link kanału zastępuje nazwę i klucz, więc przy nim tych dwóch pól się nie sprawdza.

Przed budowaniem Builder zbiera uwagi wszystkich stron:

- **dwie strony o tej samej nazwie** (także „BLUFOR” i „blufor”) — budowanie się nie zacznie, bo paczka jednej
  nadpisałaby paczkę drugiej w `out\<STRONA>\`;
- pozostałe uwagi — lista i pytanie „Zbudować paczki mimo to?”.

Karta „Stan paczki” w prawej kolumnie ma wiersz **„Do poprawienia”** z tymi samymi uwagami.

## Ważność paczek

Jedno pole w sekcji 1: **„Ważność paczek w godzinach (0 = bez limitu)”**, z przyciskami ∞ Bez limitu, 6 h, 24 h, 48 h.
Błędna wartość podświetla pole na czerwono i nie zmienia ustawienia.

Wcześniej ważność była w trzech miejscach: dwa pola w sekcji 1 (paczka 24, QR 2) i pole w karcie strony.
Ekran pokazywał 24 h, a dopóki nikt nie wpisał nowej wartości, paczki budowały się bez limitu. Ustawienie żyło
tylko w pamięci, a kod przy każdym kliknięciu w oknie szukał pól ważności po ich nazwach i podpowiedziach.
Teraz wartość zapisuje się w projekcie (`packageValidityHours`) i trafia do paczki (`neverExpires`, `expiresAt`).
Projekty bez tego pola otwierają się z wartością 0 i nie pokazują przez to znacznika ●.

## Sekcja 1 i prawa kolumna

- Zniknęły pola „Profil” (Event / MILSIM / Training) i „Opis” — nic ich nie czytało.
  Nazwa wydarzenia nie ma już przykładowego tekstu „MILSIM 2026” (ze znakiem niewidocznym w środku).
- Zniknęła karta „Podgląd zawartości” z rozmiarami folderów. Co trafi do paczki, pokazuje karta „Stan paczki”.

## Koniec z przykładowymi danymi

Nowy projekt i nowa strona dostają:

- **własny losowy klucz** — wcześniej klucz był pusty, czyli w paczce publiczny klucz Meshtastic;
- **pustą tabelę kanałów głosowych** z dopiskiem „Brak kanałów — dodaj je, jeśli mają trafić do paczki i na plakat.”
  „+ Dodaj kanał” tworzy wiersz bez częstotliwości i tonu;
- **pusty telefon alarmowy** — zamiast zmyślonego „+48 600 000 000”;
- **bez pliku `strefa_<strona>.kml`** z punktem „HQ” w stałym miejscu.

Projekty zapisane wcześniej, przy otwarciu:

- strona bez klucza dostaje losowy klucz (strony z kluczem — bez zmian);
- numer „+48 600 000 000” znika;
- znikają przykładowe kanały głosowe, które dawny Builder dopisywał stronie bez kanałów **przy każdym otwarciu**
  (trzy wiersze: „Dowodzenie HQ”, „Taktyczny Alfa 145.500 MHz” albo „Taktyczny Bravo 433.450 MHz”, „Ratunkowy ORGA”) —
  tylko gdy wszystkie trzy są nietknięte; zmieniona choćby jednym polem tabela zostaje;
- plik `factions\<strona>\strefa_<strona>.kml` jest usuwany tylko wtedy, gdy ma dokładnie przykładową treść.

Po takim otwarciu pasek boczny pokaże ● — zapisz projekt, żeby zmiany trafiły do pliku.

Rozkaz OPORD, który pobiera kanały ze stron, nie dopisuje już „Dowodzenie HQ, CH 1” stronie bez kanałów.

## Wi-Fi: serwer tylko na żądanie, adresy z tokenem

- Serwer paczek w sieci Wi-Fi/LAN startował przy każdym budowaniu. Teraz startuje dopiero po
  „📡 Dystrybucja LAN/WIFI Hotspot” i działa do zamknięcia Buildera.
- Adres paczki to `http://<IP>:8766/deploy/<token>.ftak` (rozkaz: `/opord/<token>.html`). Token to 32 losowe znaki,
  osobny dla każdej strony i nowy przy każdym uruchomieniu Buildera. Wcześniej w adresie była nazwa strony —
  każdy w tej samej sieci mógł pobrać paczkę przeciwnika razem z jej kluczem. Adres z nazwą strony daje teraz 404.
  Telefon czyta oba rodzaje adresów tak samo, więc nie wymaga zmian.

## Plakat A4

- Tabela kanałów tylko z kanałami strony. Wcześniej strona bez kanałów dostawała na plakacie przykładowe częstotliwości.
- Pasek SOS tylko z wpisanym numerem. Wcześniej był tam zmyślony numer albo napis „Kanał Ratunkowy ORGA”.
- Krok 3 instrukcji odsyła do tabeli kanałów tylko wtedy, gdy tabela jest na plakacie.
- W wierszu parametrów zamiast trybu i strategii PLI (tych pól nie ma już w karcie) jest liczba skoków (hop limit).

## Porządki w kodzie

- Usunięte podpinanie budowania do **każdego** przycisku, którego napis zawierał „ftak”, „zbuduj paczk” albo
  „build package” (jedno kliknięcie mogło uruchomić budowanie dwa razy).
- Usunięta obsługa ważności przy każdym kliknięciu w oknie.
- Pole nazwy wydarzenia Builder znajduje po nazwie z XAML, a folder `source` to ostatnio używany folder projektu
  (albo domyślny projekt roboczy). Wcześniej zgadywał je z napisów i tekstu pól w oknie.
- „Zapisz jako…” w ścieżce bez starego przycisku zapisu składało okrojony projekt z wersji na dysku (bez części pól
  stron). Teraz zapisuje pełny bieżący stan, tak jak Ctrl+S, a błąd zapisu pokazuje komunikat.
- Reguły bez WPF (adres serwera, klucz, nazwa i link kanału, link rejestracji, region, ważność, przykładowe dane,
  tokeny Wi-Fi, migawka do budowania) są w `Services/Shell/PackageGeneratorSpec.cs` i mają testy.

## Do sprawdzenia

1. Zmień nazwę strony, **nie zapisuj**, kliknij „⚡ Zbuduj paczkę strony” → paczka w `out\<NOWA NAZWA>\`,
   w `config.json` nowa nazwa.
2. Nowy projekt → w każdej karcie „✓ klucz prywatny (AES-256)”, pusta tabela kanałów, pusty telefon SOS.
3. Adres serwera `https://tak.example.org` → czerwona uwaga pod polem; budowanie pyta „Zbudować paczki mimo to?”.
4. Dwie strony „BLUFOR” i „blufor” → budowanie się nie zaczyna, komunikat mówi dlaczego.
5. Ważność 24 → zapisz, zamknij, otwórz projekt → 24; w `config.json` paczki `neverExpires: false`.
   Wpisz „abc” → czerwona ramka.
6. Zbuduj paczki **bez** „Dystrybucji” → telefon nie połączy się z `http://<IP>:8766`.
   „📡 Dystrybucja LAN/WIFI Hotspot” → QR z adresem `/deploy/<32 znaki>.ftak`, telefon pobiera paczkę ze skanu.
7. Stary projekt (sprzed tej zmiany) → numer +48 600 000 000 zniknął, strony bez klucza mają klucz,
   pasek pokazuje ● → zapisz.
8. Stary projekt z regionem US_915 → karta pokazuje US; płytka z programatora USB dostaje region US.
9. Przełącz język na angielski → karta i uwagi pod polami po angielsku.
10. Plakat strony bez telefonu → brak wiersza SOS.

Strony utworzone dawnym przyciskiem „Nowy projekt” mogą mieć jeden przykładowy kanał
(„Dowodzenie HQ, CH 1” albo „Dowodzenie OPFOR, CH 5”). Ten zostaje, bo dało się go zmienić lub usunąć — sprawdź go.

## Pliki

| Plik | Zmiana |
|---|---|
| `Services/Shell/PackageGeneratorSpec.cs` | nowy: reguły pól, region, ważność, przykładowe dane, tokeny Wi-Fi, migawka |
| `Services/TacticalConfigPanel.cs` | nowa karta strony, ważność w projekcie, czyszczenie przykładów, plakat |
| `Services/BuilderUiSimplifier.cs` | budowanie z bieżącego stanu, sprawdzenie przed budowaniem, serwer Wi-Fi z tokenami |
| `Services/BuilderCoreSidebarAndProjectEngine.cs` | stan zerowy bez atrap, usuwanie przykładowych KML, „Zapisz jako…”, OPORD bez zmyślonego kanału |
| `Services/Shell/PackageStatusSpec.cs` | wiersz „Do poprawienia” w karcie „Stan paczki” |
| `Services/Shell/ProjectShellSpec.cs` | ważność 0 = brak pola w porównaniu projektu |
| `Services/MeshtasticUsbProvisionerView.cs` | region z tej samej tabeli co karta |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | sekcja 1: nazwa i ważność; bez profilu, opisu i podglądu zawartości |
| `Resources/Strings.*.xaml`, `Services/BuilderLocalizationAndOpordEngine.cs` | teksty i tłumaczenia |
| `FieldTakHub.Builder.Tests/PackageGeneratorStage1Tests.cs` | testy reguł etapu 1 |
