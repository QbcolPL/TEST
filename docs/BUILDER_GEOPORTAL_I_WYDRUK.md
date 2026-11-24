# Builder: szybszy Geoportal i wydruk z ramką kartograficzną

Zmiany w edytorze mapy taktycznej Buildera (Windows). Dotyczą dwóch rzeczy: czasu doczytywania podkładów
z Geoportalu oraz nowego wydruku PDF z ramką w stylu arkuszy taktycznych 1:25 000 (np. T.REX).

## 1. Doczytywanie Geoportalu

### Dlaczego było wolno

- Ortofotomapa, mapa TOPO i ortofotomapa HR były pobierane jako **jeden duży obraz WMS dla całego widoku**.
  Każde przesunięcie mapy (nawet o kilka metrów) oznaczało nowe zapytanie o cały obraz, który serwer GUGiK
  renderował od zera. Pamięć podręczna praktycznie nie działała, bo adres zapytania był za każdym razem inny.
- Cieniowanie LiDAR (NMT) próbowało kolejno 4 adresów z limitem 3,8 s na każdy, zanim sięgnęło po źródło
  zapasowe. Obraz był pobierany w układzie EPSG:2180 i nakładany na mapę Web Mercator (lekkie przesunięcie).

### Co jest teraz

| Warstwa | Źródło | Zapas |
|---|---|---|
| Geoportal ORTO (standard) | gotowe kafelki **WMTS** GUGiK w EPSG:3857 (najszybsze — kafelki są już wyrenderowane) | WMS GUGiK dla jednego kafelka → ESRI World Imagery |
| Geoportal ORTO HIGH-RES | od poziomu 17: WMS HR po jednym kafelku 512 px; puste pola uzupełnia ortofotomapa standardowa; poniżej poziomu 17 — WMTS standard | — |
| Geoportal TOPO | WMS po jednym kafelku 384 px | ESRI World Topo Map |
| LiDAR (nakładka i podkład) | WMS cieniowania NMT po jednym kafelku 512 px w EPSG:3857, składane w mozaikę z **jednym kontrastem dla całego widoku** (bez szwów) | ESRI World Hillshade |

- Każdy kafelek ma stały adres, więc działa **pamięć podręczna w RAM i na dysku**, kafelki są pobierane
  równolegle, a przy przesuwaniu mapy pobierane są tylko nowe kafelki na krawędzi.
- Mozaika LiDAR ma 40% zapasu wokół widoku — krótkie przesunięcia nie wymagają jej ponownego składania.
- Pamięć na dysku: `%LOCALAPPDATA%\FieldTakHub\GeoCacheV26`, limit 1,5 GB (przy starcie usuwane są najstarsze
  pliki). **🔄 Odśwież** czyści pamięć RAM; z wciśniętym **Shift** czyści też dysk.
- Pamięć RAM na zdekodowane kafelki: 384 MB, najstarsze wypadają pojedynczo (wcześniej całość była czyszczona
  co 700 kafelków, co powodowało migotanie i ponowne pobieranie).
- Płótno mapy odświeża się raz na cykl, a nie po każdym przychodzącym kafelku.
- Powyżej maksymalnego poziomu źródła (np. OpenTopoMap: 17) kafelki są powiększane zamiast pustych pól.
- Bezpieczeństwo: włączona pełna weryfikacja certyfikatów TLS (wcześniej była wyłączona) i uczciwy
  User-Agent aplikacji zamiast udawania przeglądarki Chrome (wymagają tego serwery OSM i Nominatim).

## 2. Wydruk z ramką kartograficzną

Przycisk w sekcji 4 edytora mapy: **🗺️ Wydruk z ramką kartograficzną (PDF, seria arkuszy)…**

1. Okno ustawia arkusz w edytorze na obszar mapy z wydruku (format „Wydruk z ramką — obszar mapy W × H mm”).
   Ekran pokazuje dokładnie ten wycinek terenu, który trafi na papier. Poprzedni format wybierzesz z listy
   „Format arkusza / mapnika”.
2. Podgląd odświeża się po każdej zmianie ustawień (używa kafelków z ekranu). Mapę i obiekty przesuwa się
   normalnie w edytorze — okno wydruku może być otwarte obok.
3. **Zapisz PDF…** pobiera podkład w rozdzielczości wydruku (150 / 200 / 300 DPI), renderuje obiekty
   taktyczne wektorowo w tej samej rozdzielczości i zapisuje PDF (jedna strona albo seria).

### Elementy arkusza (wszystkie można wyłączyć lub zmienić)

| Element (jak na arkuszach T.REX) | Ustawienie |
|---|---|
| Linia ramki z podziałką co 1/10 oczka siatki na zewnątrz | „Linia ramki”, „Podziałka na linii ramki” |
| Siatka UTM/MGRS co 1 km (1:25 000) z kreskami co 100 m | „Siatka UTM/MGRS”, „Oczko siatki” (auto lub 100 m–10 km), kolor, grubość |
| Opisy linii siatki na białej obwódce, pionowe po bokach | „Opisy linii wewnątrz ramki” / „na marginesie”, styl „35” albo „535 000”, wielkość |
| Logo + nazwa arkusza MGRS, np. „34UCA 58 30” (kwadrat 1 km rogu SW) | logo Field TAK Hub / własny plik / brak; tytuł MGRS albo własny |
| Podtytuł „W POBLIŻU …” / „22 KM NA PD OD …” | tekst własny, nazwa z wyszukiwarki albo przycisk **📍 Z OSM** (Nominatim) |
| Adres strony, linia „Deklinacja WMM2025 · mapa z data” | pole tekstowe, przełącznik |
| Pola SKALA, UKŁAD, SZER./DŁ. narożnika SW, dwa pola własne (np. PODKŁAD, POZIOMICE) | przełączniki i pola tekstowe |
| Podziałka km (pierwszy odcinek podzielony na 5) i w milach | przełączniki; długość dobierana do skali i szerokości stopki |
| Notka „Jeśli podziałka km nie ma 100 mm, skala jest błędna” | przełącznik — liczba mm odpowiada rzeczywistej długości podziałki |
| Kąt PT–PM, PT (zbieżność), PM (deklinacja), zmiana roczna, diagram „nie w skali” | przełącznik; model WMM2025 wbudowany w program |
| Kod QR | `geo:` środka, Google Maps, OpenStreetMap, MGRS środka, własny tekst, brak |
| Pionowe opisy na marginesach (prawa do danych / uwagi) | przełączniki, tekst własny albo automatyczny |
| Obiekty z edytora, legenda, strzałka północy, podziałka i siatki z ekranu | przełączniki w sekcji 5 |
| Seria arkuszy (kolumny × wiersze, zakładka %) | sekcja 6 — każdy arkusz ma własną nazwę, narożnik SW i diagram |
| Papier A4/A3/A5/Letter/własny, orientacja, marginesy, stopka, DPI, jakość JPEG, język PL/EN | sekcja 1 |

Przycisk **🎯 Wyrównaj róg SW arkusza do przecięcia linii siatki** przesuwa mapę tak, by lewy dolny róg
ramki leżał na przecięciu linii siatki — jak na arkuszach T.REX.

### Ważne

- Mapa jest w odwzorowaniu Web Mercator (północ geograficzna u góry) — tak samo jak w edytorze. Linie siatki
  UTM/MGRS są dokładne, ale obrócone o zbieżność południków (w Polsce do ok. 2,3° na skraju strefy).
  Wartość pokazuje diagram PT/PM i opis na prawym marginesie.
- Deklinacja jest liczona modelem WMM2025 (NOAA NCEI / BGS), ważnym do końca 2029 r. Zgodność z oficjalnymi
  wartościami testowymi NOAA sprawdza test jednostkowy.
- Strony PDF są obrazami JPEG (tekst nie jest zaznaczalny); A4 w 300 DPI to ok. 3–6 MB na stronę.
- Ustawienia wydruku zapisują się w `%LOCALAPPDATA%\FieldTakHub\map-print-settings.json`.

## 3. Nowe moduły (oddzielone od istniejącego kodu)

| Plik | Zadanie | Testy |
|---|---|---|
| `Services/MapPrint/GeoMagneticModel.cs` | model WMM2025: deklinacja, inklinacja, zmiana roczna | wartości wzorcowe NOAA |
| `Services/MapPrint/MapGridMath.cs` | strefa UTM, zbieżność, MGRS, nazwa arkusza, opisy siatki, podziałki | tak |
| `Services/MapPrint/MapSheetLayout.cs`, `MapPrintSettings.cs` | układ strony, ustawienia z zapisem JSON | tak |
| `Services/MapPrint/MapSheetFacts.cs` | dane arkusza (SW, PT, PM, kąt PT–PM, QR) | arkusz 16SDE 34 67 z T.REX |
| `Services/MapPrint/PdfImageDocumentWriter.cs` | wielostronicowy PDF z obrazów JPEG | struktura i tablica xref |
| `Services/MapPrint/MapFrameRenderer.cs` | rysowanie ramki, siatki i stopki | — (WPF) |
| `Services/MapImagery/MercatorTileMosaic.cs` | składanie obrazu z kafelków, operacje na pikselach | położenie kafelków, krycie |
| `Services/TacticalMapGeneratorView.Imagery.cs` | klucze kafelków, HR + standard, mozaika LiDAR, podkład do wydruku | — |
| `Services/TacticalMapGeneratorView.Print.cs` | okno wydruku, seria arkuszy, zapis PDF | — |

## 4. Do sprawdzenia po zbudowaniu

1. Geoportal ORTO: przesuwanie mapy doczytuje tylko brzegi; ponowne otwarcie tego samego terenu — od razu z dysku.
2. LiDAR jako nakładka i jako podkład: brak szwów między kafelkami, krótkie przesunięcia bez przebudowy.
3. Geoportal HIGH-RES w mieście przy skali ok. 1:2 500.
4. Wydruk A4 1:25 000: zmierz linijką podziałkę km — ma mieć 100 mm. Siatka co 1 km z kreskami co 100 m.
5. Seria 2 × 2, PDF otwiera się w Edge/Adobe; wydruk w „Rzeczywistym rozmiarze” (bez dopasowania do strony).
