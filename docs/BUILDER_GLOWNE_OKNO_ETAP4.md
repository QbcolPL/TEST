# Okno główne Buildera: projekt, skróty i prawdziwy status paczki (etap 4 uproszczenia interfejsu)

Etapy 1–3 dotyczyły generatora map. Etap 4 porządkuje to, co jest wspólne dla całego okna: pasek boczny
z projektem, skróty klawiszowe, zamykanie okna oraz ekran generatora paczek (karty, które pokazywały stałe wartości).

## Skróty całego okna

Działają w każdym module. Pole tekstowe i generator map obsługują swoje klawisze pierwsze, więc np. Ctrl+Z
w polu tekstowym nadal cofa pisanie.

| Skrót | Działanie |
|---|---|
| Ctrl+S | zapisz projekt do bieżącego pliku `.fthproj` (projekt bez pliku — okno wyboru nazwy) |
| Ctrl+Shift+S | zapisz projekt jako… (zawsze pyta o plik) |
| Ctrl+O | otwórz projekt (jeśli bieżący ma niezapisane zmiany — najpierw pytanie, czy je zapisać) |
| Ctrl+N | nowy projekt (to samo okno potwierdzenia co przycisk w pasku bocznym) |
| Alt+1 … Alt+4 | moduły: paczki, mapy, OPORD, programator USB |
| F1 | ściągawka wszystkich skrótów (okno główne + generator map) |

- Moduły przełącza **Alt**+cyfra, bo Ctrl+1…5 to zakładki panelu generatora map (etap 3).
  Alt z cyframi klawiatury numerycznej nie działa celowo — w polach tekstowych Windows wpisuje nim znaki (kody Alt).
- Skróty są wypisane szarym tekstem przy przyciskach paska bocznego. Na dole paska jest przycisk
  „Skróty klawiszowe” (F1). W ściągawce generatora map („?” nad mapą) doszedł wiersz o skrótach całego okna.

## Stan projektu: nazwa pliku i niezapisane zmiany

- Pasek boczny pokazuje plik projektu: „Projekt: Operacja.fthproj”. Projekt bez pliku: „Projekt: nowy (jeszcze bez pliku)”.
- Niezapisane zmiany: kropka i żółty kolor („● Projekt: Operacja.fthproj”), a na pasku tytułu okna gwiazdka
  („*Operacja.fthproj — Field TAK Hub Builder 2.3.0”). Podpowiedź nad nazwą pokazuje pełną ścieżkę pliku.
- Znacznik porównuje **treść** projektu (strony konfliktu, nazwa wydarzenia, mapa z obiektami i sektorami ognia,
  ustawienia arkusza) z tym, co jest w pliku. Cofnięcie zmian (Ctrl+Z na mapie) do stanu z pliku usuwa znacznik.
  Nie liczą się: otwarta zakładka panelu mapy, tekst wyszukiwania, poziom kafelków i ostatnio użyte foldery.
  Przesunięcie mapy albo zmiana skali **jest** zmianą — przesuwa arkusz, który trafi do wydruku i eksportu.
- „Zapisz projekt” w pasku bocznym i Ctrl+S zapisują wprost do bieżącego pliku, bez okna wyboru. Zapis idzie
  najpierw do pliku tymczasowego obok i dopiero potem podmienia projekt, więc przerwany zapis nie psuje pliku.
- Po ponownym uruchomieniu Builder przywraca ostatni stan z autozapisu (tak było i wcześniej), a teraz także
  wraca do tego samego pliku projektu: pasek boczny pokazuje jego nazwę, Ctrl+S zapisuje do niego,
  a znacznik ● wie, czy przywrócony stan różni się od pliku. Wcześniej po restarcie pasek pokazywał
  „Projekt: Nowy (Stan zerowy)”, choć na ekranie był przywrócony projekt.

## Zamykanie okna i otwieranie innego projektu

- Okno zamyka się bez pytania, gdy wszystko jest zapisane w pliku (albo nic się nie zmieniło od otwarcia).
  Wcześniej okno „zapisać przed zamknięciem?” pokazywało się przy każdym zamknięciu.
- Przy niezapisanych zmianach pytanie mówi, czego dotyczy: „💾 Zapisz i zamknij (Ctrl+S)” zapisuje do bieżącego
  pliku bez okna wyboru (projekt bez pliku: „Zapisz jako… i zamknij”), „Zamknij bez zapisu”, „Anuluj” (także Esc).
  Autozapis i tak przywróci bieżący stan przy następnym uruchomieniu — okno mówi to wprost.
- „Otwórz projekt” (Ctrl+O) przy niezapisanych zmianach najpierw pyta: Tak (zapisz), Nie (porzuć), Anuluj.
  Autozapis nie chroni tu zmian, bo po otwarciu przechowuje już nowy projekt.
- „Nowy projekt”: „💾 Zapisz obecny i utwórz nowy” zapisuje do bieżącego pliku bez okna wyboru
  (projekt bez pliku — okno wyboru nazwy).

## Nowy projekt nie kasuje map z folderu source

„Nowy projekt” usuwał **wszystkie** pliki z `source\maps` — także ręcznie dodane mapy offline (.mbtiles, .sqlite),
bez możliwości odzyskania. Teraz przenosi je do folderu `archiwum\maps_RRRRMMDD_GGMMSS` obok folderu `source`
(poza nim, więc nie trafiają do kolejnych paczek). Plik, którego nie da się przenieść (np. otwarty w innym
programie), zostaje na miejscu. Okno potwierdzenia nowego projektu mówi o tym w ostatnim zdaniu.

## Generator paczek: ekran pokazuje to, co naprawdę trafi do paczki

### Status paczki

Karta pokazywała zawsze to samo: duży ✓, „Gotowa do generowania” i sześć wierszy „✓ Skonfigurowany”,
„3 pluginy”, „2 pakiety” — niezależnie od zawartości folderu. Teraz wiersze liczy Builder z folderu `source`
i stron konfliktu, tymi samymi regułami co budowanie paczki:

| Wiersz | Co pokazuje |
|---|---|
| Strony konfliktu | liczba i nazwy (bez stron: „!” — żadna paczka nie powstanie) |
| ATAK (APK), Meshtastic (APK) | nazwa pliku APK albo „brak” (wtedy aplikacja musi już być na telefonie) |
| Pluginy ATAK | liczba pozostałych plików .apk |
| Mapy offline | liczba plików .mbtiles / .sqlite i ich rozmiar |
| Nakładki i trasy | liczba plików .kmz / .kml / .gpx |
| Certyfikaty i ustawienia, Inne dołączane | tylko gdy są: .p12 / .pref oraz .zip / .html |
| Serwer TAK | adres serwera, „1 z 2 stron” albo „brak — tylko mesh” |
| Pominięte pliki | pliki, których paczka **nie** zabierze (np. .tif, .pdf, .geojson) — z listą rozszerzeń, które zabiera |

- ✓ — jest, ○ — brak, ale paczka powstanie, ! — uwaga. Najechanie na wiersz pokazuje szczegóły
  (np. adres serwera każdej strony albo listę plików APK).
- Pliki robocze Buildera (.json, .txt, .md, .xml, .cfg, .ini, .log) nie są zgłaszane jako pominięte — nikt ich w paczce nie oczekuje.
- Pliki z `source\factions\<strona>\` są liczone w podsumowaniu, ale do paczki trafiają tylko dla swojej strony.

### Ostatnie paczki .ftak

Karta „Generowany plik” zawsze pokazywała „—”: nic jej nie aktualizowało, a przycisk „Zapisz QR PNG” zapisywał
kod z dawnego trybu dystrybucji. Teraz pokazuje najnowszą paczkę z folderu `out\` (nazwa, folder, rozmiar,
„dziś 14:32”, liczba pozostałych paczek), a przycisk **„📂 Otwórz folder paczek”** otwiera jej folder
(bez paczek — folder `out\` obok `source`).

### Podgląd zawartości

Rozmiary „Mapy” i „Pluginy” nie docierały do karty „Podgląd zawartości”: Builder szukał wierszy po napisach,
a wcześniej w oknie są inne wiersze „Mapy” / „Pluginy” (sekcja „2. Zawartość paczki”, dawna karta statusu),
więc rozmiar lądował tam. Teraz karta jest wypełniana po nazwach pól — wszystkie sześć rozmiarów trafia na miejsce.

### Sekcja „2. Zawartość paczki” ukryta

Sześć kart tej sekcji (ATAK, Pluginy, Mapy, Overlays, Meshtastic, Serwer) pokazywało stałe wartości
(„v5.6.0.13”, „3 pluginy”, „2 pakiety”, „1 pakiet”, „Skonfigurowany”, „OpenTAKServer”), a ich przełączniki niczego
nie zmieniały w paczkach stron konfliktu. „★ Wybierz profil zalecany” nadpisywał nazwę wydarzenia przykładem
„MILSIM 2026”. Prawdziwą zawartość pokazuje teraz karta „Status paczki”. Sekcja jest tylko ukryta
(`Visibility="Collapsed"` w `MainWindow.xaml`) — można ją przywrócić jednym atrybutem.

### Dolny pasek przycisków

Zniknął pasek pod stronami konfliktu: „⌫ Wyczyść wszystko” niczego nie czyścił (tylko sprawdzał i tworzył
foldery projektu, co Builder robi sam), a „Generuj paczkę .FTAK” i „⌄” były ukrywane przy starcie.
Paczki buduje pasek „🎯 Generator paczek .ftak” nad stronami konfliktu („⚡ Buduj Wszystkie Strony”).

## Do sprawdzenia po zbudowaniu

1. Zmień nazwę strony konfliktu albo przesuń obiekt na mapie: w pasku bocznym pojawia się „●”, na pasku
   tytułu „*”. Ctrl+Z na mapie do stanu z pliku — znacznik znika (po ułamku sekundy).
2. Ctrl+S w projekcie z plikiem: zapis bez okna, znacznik znika. Ctrl+Shift+S: okno „Zapisz jako”.
3. Alt+2 — generator map, Alt+1 — paczki. W generatorze map Ctrl+1…5 dalej przełącza zakładki panelu.
4. F1 — ściągawka; F1 albo Esc ją zamyka.
5. Zamknij Builder bez zmian — zamyka się bez pytania. Zmień coś i zamknij — pytanie z nazwą pliku.
6. Uruchom ponownie: pasek boczny pokazuje ten sam plik projektu, a „●” tylko wtedy, gdy zamknięto bez zapisu.
7. Wrzuć do `source\maps` plik .tif: w „Status paczki” pojawia się „! Pominięte pliki: 1 · .tif”.
   Wrzuć plik .mbtiles: „Mapy offline” pokazuje go, a „Podgląd zawartości → Mapy” jego rozmiar.
8. „⚡ Buduj Wszystkie Strony” — karta „Ostatnie paczki .ftak” pokazuje nową paczkę; „📂 Otwórz folder paczek” otwiera jej folder.
9. „Nowy projekt” z plikami w `source\maps` — pliki są w `archiwum\maps_…` obok `source`.

## Kod

| Plik | Zadanie |
|---|---|
| `Services/BuilderShell.cs` | skróty okna, znacznik niezapisanych zmian (odcisk stanu, sprawdzany chwilę po zmianie), tytuł okna, plik projektu w autozapisie sesji, ściągawka F1 |
| `Services/PackageStatusView.cs` | karty „Status paczki” i „Ostatnie paczki .ftak”, przycisk „Otwórz folder paczek” |
| `Services/Shell/ProjectShellSpec.cs` | `ProjectSignature` (odcisk treści projektu), `ProjectTitleText` (nazwa w pasku i tytule), `ShellShortcuts`, `MapsArchive` |
| `Services/Shell/PackageStatusSpec.cs` | wiersze statusu z listy plików i stron (te same reguły co budowanie paczki), opis ostatniej paczki, odmiana „1 plik / 2 pliki / 5 plików” |
| `Services/BuilderCoreSidebarAndProjectEngine.cs` | nazwa projektu z jednego miejsca, zapis do bieżącego pliku, pytanie przed otwarciem innego projektu, archiwum map przy nowym projekcie, skróty przy przyciskach, przycisk „Skróty klawiszowe” |
| `Services/TacticalConfigPanel.cs` | zapis do znanego pliku (przez plik tymczasowy), pytanie przy zamykaniu tylko przy zmianach, lista plików source i paczek z out\ dla kart statusu, „Podgląd zawartości” po nazwach pól |
| `MainWindow.xaml`, `Resources/Strings.*.xaml` | nowa karta statusu, „Ostatnie paczki .ftak”, ukryte: sekcja „2. Zawartość paczki” i dolny pasek |
| `FieldTakHub.Builder.Tests/BuilderShellTests.cs` | testy: odcisk projektu, nazwa i tytuł, skróty, archiwum map, wiersze statusu, odmiana liczebników, ostatnia paczka |
