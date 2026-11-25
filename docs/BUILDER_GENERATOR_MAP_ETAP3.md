# Generator map: panel w zakładkach i jedno menu „Eksport…” (etap 3 uproszczenia interfejsu)

Panel boczny generatora map nie jest już jedną długą kolumną pięciu sekcji. Ma pięć zakładek w kolejności pracy
nad mapą, a wszystkie formaty wyjściowe są w jednym menu „📤 Eksport…”.

## Zakładki

| Zakładka | Skrót | Co jest w środku |
|---|---|---|
| 🗺 Mapa | Ctrl+1 | **Teren gry i strony konfliktu** (strony, wyszukiwanie miejsca), **Arkusz i skala** (format, orientacja, skala 1:X), **Podkład mapy** (Geoportal / LiDAR / Google / OSM, rzeźba LiDAR, rozjaśnienie i przyciemnienie podkładu) |
| 📍 Obiekty | Ctrl+2 | **Nowe obiekty** (symbol NATO, kolor), **Na mapie** (import .KMZ / .GPX, sektory ognia, lista wszystkiego na mapie, współrzędne i szczegóły zaznaczonego elementu), **Wygląd obiektów** (etykiety, uchwyty obrotu, maska poza AO) |
| ▦ Siatka | Ctrl+3 | **Siatka** (MGRS / sektorowa / WGS84, podział oczek, opisy, podziałka sektorowa A1, B2…), **Wygląd siatki i opisów** (kolory, grubość, czcionka, krycie, obrys) |
| 📋 Legenda | Ctrl+4 | znacznik Północy, podziałka skali, legenda: włączanie, położenie, treść i wymiary legendy |
| 📤 Eksport | Ctrl+5 | lista formatów (to samo co menu „Eksport…”) z trybem GRG przy .KMZ oraz karta **Mapa offline** (ustawienia, szacunek, postęp, przerwanie) |

- Zakładki są zawsze na górze panelu; przewija się tylko zawartość. Każda zakładka pamięta, dokąd była przewinięta.
- Zaznaczenie elementu na mapie (znacznik, wierzchołek, uchwyt ✥, sektor ognia), postawienie nowego obiektu, F2
  i „Pokaż na liście” przełączają na zakładkę **Obiekty** — tam są szczegóły zaznaczonego elementu.
- Wybrana zakładka zapisuje się z sesją i projektem. Pierwsze uruchomienie zaczyna od zakładki **Mapa**.

## Jedno menu „📤 Eksport…”

Stały przycisk na dole panelu (Ctrl+E) otwiera menu z każdym formatem — wcześniej siedem przycisków w sekcjach 4 i 5:

| Grupa | Pozycja |
|---|---|
| Pliki | 📦 Mapa .KMZ… · ⌚ Trasy i punkty .GPX… · 🖨️ PDF arkusza… · 🗺️ Wydruk z ramką kartograficzną… · 🎯 Sektory ognia .KMZ… · 💾 Projekt roboczy (.fthproj)… |
| Do paczki .ftak | ⚡ Arkusz .KMZ / .GPX do paczki · ⚡ Mapa offline do paczki (z potwierdzeniem — bez okna zapisu pobieranie ruszyłoby od razu) |
| Mapa offline | 📥 Mapa offline jako plik… |

- Każda pozycja ma jednozdaniowy opis „co dostaniesz”. Esc albo klik obok zamyka menu.
- Budowa mapy offline przełącza na zakładkę **Eksport**, gdzie widać postęp i przycisk „⏹ Przerwij”.
- Ostatnia pozycja menu — „Ustawienia eksportu” — otwiera zakładkę **Eksport**.
- Przycisk „📂 Wczytaj KMZ/GPX” z dawnej sekcji 4 zniknął: wczytywanie jest w zakładce **Obiekty**
  („📂 Wczytaj i edytuj .KMZ / .KML / .GPX”, także projekty .fthproj / .json).

## Gdzie co trafiło (stare sekcje → zakładki)

| Dawniej | Teraz |
|---|---|
| 1. Strony konfliktu, format arkusza i skala | Mapa |
| 2. Siatka MGRS / sektorowa, kolory RGB | Siatka (rozjaśnienie i przyciemnienie podkładu — Mapa) |
| 3. Obiekty na mapie i sektory ognia | Obiekty |
| 4. Elementy mapy i eksport | Legenda (Północ, podziałka, legenda) i Eksport (tryb GRG .KMZ i wszystkie przyciski zapisu) |
| 5. Generator map offline | Eksport → karta „Mapa offline” |

## Kod

| Plik | Zadanie |
|---|---|
| `Services/TacticalMapGeneratorView.LeftPanel.cs` | kolumna panelu: zakładki, strony, pamięć przewinięcia, menu „Eksport…”, karta formatów |
| `Services/MapEditor/LeftPanelSpec.cs` | `LeftPanelTabs` (zakładki, Ctrl+cyfra, zapisana zakładka) i `ExportCatalog` (formaty w grupach) — testy: `MapLeftPanelTests` |
| `Services/MapEditor/MapEditorUi.cs` | przyciski zakładek, wiersze listy eksportu, podtytuły grup |
| `Services/TacticalMapGeneratorView.cs` | `RebuildLeftControlsPanel` składa karty w zakładki zamiast w jedną kolumnę |
| `Services/TacticalMapGeneratorView.Engine.cs` | karta siatki podzielona na „Siatka” i „Wygląd siatki i opisów”; jasność podkładu osobno (zakładka Mapa) |
