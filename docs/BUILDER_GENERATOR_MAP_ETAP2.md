# Generator map: cofanie zmian i jedna lista obiektów (etap 2 uproszczenia interfejsu)

Etap 2 dokłada cofanie zmian i zbiera wszystko, co jest na mapie, w jednej liście w sekcji 3 — razem z sektorami
ognia, które do tej pory miały osobny panel w sekcji 2.

## Cofanie i ponawianie

| Skrót / przycisk | Działanie |
|---|---|
| Ctrl+Z, ↶ nad mapą | cofnij ostatnią zmianę |
| Ctrl+Y, Ctrl+Shift+Z, ↷ nad mapą | ponów cofniętą zmianę |
| Ctrl+Z w trakcie rysowania kształtu | usuń ostatni postawiony punkt (przy pierwszym punkcie — anuluj kształt) |

- Cofa się każda zmiana obiektów i sektorów ognia: postawienie, przesunięcie, obrót, zmiana nazwy, koloru,
  wielkości, typu, uproszczenie kształtu, usunięcie, „Wyczyść listę”, import .KMZ / .KML / .GPX.
- Jedno przeciągnięcie, jedno przesunięcie suwaka albo jedna wpisana nazwa to jeden krok (krok powstaje po
  chwili bez zmian, a w trakcie przeciągania — dopiero po puszczeniu przycisku myszy).
- Historia ma 60 kroków. Nowa zmiana po cofnięciu kasuje „ponów” — jak w innych programach.
- Wczytanie projektu, przywrócenie sesji i nowy projekt zaczynają historię od nowa.
- W polu tekstowym Ctrl+Z działa jak zwykle w Windows: cofa pisanie w tym polu, nie zmiany na mapie.
- Nie cofa się ustawień siatki, warstw, skali i wydruku — tylko treść mapy.
- Podpowiedź przycisków ↶ ↷ mówi, ile kroków jest w historii; przycisk bez kroków jest przygaszony.

## Jedna lista wszystkiego na mapie

Sekcja 3 ma jedną listę: najpierw obiekty (znaczniki NATO, kropki, teren gry, sektory, trasy) w kolejności
rysowania, potem — pod nagłówkiem „🎯 Sektory ognia” — sektory ognia. Nad listą jest licznik, np.
„Na mapie: 3 obiekty · 1 sektor ognia”.

Każdy wiersz ma ten sam układ:

| Element | Obiekt mapy | Sektor ognia |
|---|---|---|
| przełącznik | Aa — etykieta z nazwą na mapie | 👁 — widoczność na mapie |
| ● | kolor (klik — paleta) | kolor (klik — paleta) |
| ikona | symbol NATO albo rodzaj kształtu | wycinek albo okrąg w kolorze sektora |
| opis | nazwa · symbol albo rodzaj i liczba punktów | nazwa · zasięg (płaski ➔ efektywny), azymut, kąt, ΔH |
| − | usuń z mapy | usuń z mapy |

- **Klik w wiersz** zaznacza element na mapie i rozwija pod listą jego szczegóły; **dwuklik** dodatkowo
  wyśrodkowuje mapę.
- **Klik na mapie** (znacznik, wierzchołek, uchwyt ✥, sektor ognia) zaznacza ten sam wiersz na liście — lista
  przewija się do niego. Klik w puste miejsce mapy albo Esc odznacza.
- **Szczegóły pod listą**: dla obiektu pełna karta (nazwa, opis w legendzie, wielkość, obrót albo typ
  i upraszczanie kształtu), dla sektora ognia — jego edytor (nazwa, kolor, broń, promień, azymut, kąt,
  ΔH z Auto-DEM, krycie) z tym samym nagłówkiem [👁] [●] [📍] [ikona] [opis] [−].
- Zaznaczony sektor ognia ma na mapie grubszy obrys, a inspektor współrzędnych pokazuje MGRS i WGS84 jego
  stanowiska („📋 Kopiuj meldunek” działa także dla sektora).
- Klawisze działają dla obiektów i sektorów ognia tak samo: Del — usuń, Ctrl+D — duplikuj, F2 — nazwa,
  F — pokaż na mapie.
- Pod przyciskami importu są przyciski sektorów ognia: „🎯 + Sektor ognia”, „📂 Sektory z .KMZ”,
  „📦 Sektory do .KMZ…” (wcześniej w sekcji 2). Sektor ognia można też postawić prawym klikiem na mapie.
- „🧹 Wyczyść listę” czyści całą listę — obiekty i sektory ognia (z potwierdzeniem; Ctrl+Z przywraca).

## Sektory ognia zapisują się z mapą

Sektory ognia są teraz częścią stanu mapy (klucz `rangeSectors` w `mapState` sesji i projektu `.fthproj`).
Wcześniej żyły tylko w pamięci: po ponownym uruchomieniu Buildera znikały, jeśli nie zapisano ich osobno do
.KMZ. Starsze projekty (bez tego klucza) wczytują się bez zmian, a „Nowy projekt” czyści także sektory ognia.

## Kod

| Plik | Zadanie |
|---|---|
| `Services/TacticalMapGeneratorView.Undo.cs` | historia zmian: kiedy powstaje krok, Ctrl+Z / Ctrl+Y, przyciski ↶ ↷ |
| `Services/MapEditor/MapEditHistory.cs` | stosy cofania i ponawiania, kopie obiektów i sektorów, podpis stanu (testy: `MapEditHistoryTests`) |
| `Services/TacticalMapGeneratorView.ObjectList.cs` | wspólna lista, wiersze, panel szczegółów, zaznaczenie wspólne z mapą |
| `Services/MapEditor/MapObjectListSpec.cs` | `MapListText` — licznik i opisy wierszy (testy: `MapUnifiedListTests`) |
| `Services/MapEditor/RangeSectorJson.cs` | zapis i odczyt sektorów ognia w stanie mapy (testy: `MapUnifiedListTests`) |
| `Services/TacticalMapGeneratorView.RangeSectors.cs` | edytor sektora ognia (jeden, pokazywany w panelu szczegółów) i jego obsługa na mapie |
| `Services/TacticalConfigPanel.cs` | zdarzenie `ContentChanged` przy każdej zmianie projektu (to samo miejsce co autozapis) |

Jak powstaje krok cofania: każda zmiana projektu woła `TacticalConfigPanel.MarkDirty()`, a każde puszczenie
przycisku myszy czy klawisza w generatorze też zleca sprawdzenie. Po 450 ms bez zmian (i bez wciśniętego
przycisku myszy) generator porównuje skrót stanu obiektów i sektorów z ostatnim zapisanym — krok powstaje
tylko wtedy, gdy treść naprawdę się zmieniła. Przesunięcie mapy nie jest zmianą sektora z kotwicą
geograficzną, więc nie tworzy kroku.
