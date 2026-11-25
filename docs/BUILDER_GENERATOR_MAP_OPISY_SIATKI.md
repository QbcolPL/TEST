# Generator map: opisy siatki przy dowolnych krawędziach i nic nie zasłania arkusza

## Co było nie tak

- Opis arkusza („📄 Arkusz: A3 … • Skala … • Wycinek terenu …”) i współrzędne kursora leżały **na** arkuszu,
  przy jego górnej krawędzi — zasłaniały górne opisy siatki. Pasek narzędzi i podpowiedź narzędzia też leżały
  na podglądzie i przy szerokim arkuszu wchodziły na mapę.
- Numery linii MGRS były tylko u góry i po lewej, listwy sektorów (A, B, C / 1, 2, 3) też, a stopnie WGS84 zawsze
  przy wszystkich czterech krawędziach — bez wyboru.

## Arkusz bez nakładek

Podgląd jest podzielony na trzy pasy:

| Pas | Co w nim jest |
|---|---|
| górny | pasek narzędzi (lewa strona) i opis arkusza |
| środkowy | sam arkusz — dopasowany do miejsca między pasami |
| dolny | podpowiedź narzędzia (lewa strona) i współrzędne kursora (prawa) |

Arkusz nigdy nie leży pod żadnym z tych elementów, więc opisy siatki są widoczne przy każdej krawędzi.
Podpowiedź i współrzędne mają po jednej linii (za długi tekst kończy się „…”), dzięki czemu arkusz nie zmienia
rozmiaru przy zmianie narzędzia ani przy ruchu myszy. Arkusz na ekranie jest przez to o kilka procent niższy.
Na eksport nie ma to wpływu: pasy nigdy nie trafiały do PDF-a — teraz po prostu nie przykrywają podglądu.

## Opisy przy krawędziach arkusza — dowolne połączenie

Zakładka **▦ Siatka** → karta „Wygląd siatki i opisów” → **„Opisy przy krawędziach arkusza”**:
cztery przełączniki **▲ Góra**, **▼ Dół**, **◀ Lewa**, **▶ Prawa**. Każde połączenie jest dozwolone, także żadna krawędź.

| Siatka | Góra / dół | Lewa / prawa |
|---|---|---|
| MGRS / UTM | numery linii pionowych (np. 334) | numery linii poziomych (np. 254) |
| Sektorowa | listwa liter A, B, C… | listwa cyfr 1, 2, 3… (gdy „Listwy A,B,C / 1,2,3” jest włączone) |
| WGS84 | długość geograficzna | szerokość geograficzna |

- Domyślnie **wszystkie cztery** krawędzie (jak na mapach topograficznych). Wybór zapisuje się z projektem
  (`gridLabelEdges` w stanie mapy); projekty zapisane wcześniej otwierają się z czterema krawędziami.
- Narożnik, w którym spotykają się opisy dwóch krawędzi, zostaje wolny — opis, który by w niego wszedł, jest pomijany.
  Kody sektorów (A1, B2…) omijają listwy przy wszystkich włączonych krawędziach.
- Znacznik Północy, podziałka i legenda stoją domyślnie przy narożnikach — teraz odsunięte od pasów opisów, żeby ich
  nie przykrywały. Element przesunięty myszą zostaje tam, gdzie go postawiono.
- Opisy są częścią arkusza, więc trafiają do wszystkiego, co zapisuje obraz arkusza z edytora: PDF, KMZ z obrazem
  mapy (tryb GRG) i mapa do paczki .ftak.
- **Wydruk z ramką** ma własne opisy w swoim oknie („Opisy linii wewnątrz ramki” — przy czterech krawędziach,
  „na marginesie” — góra i lewa); ta zmiana ich nie dotyczy.

## Poprawka: „Siatka Sektorowa”

Wybór „Siatka Sektorowa (A1, B2, C3...)” jako układu współrzędnych rysował siatkę MGRS, a siatkę sektorową tylko
wtedy, gdy była włączona jako nakładka. Builder sprawdzał, czy nazwa zaczyna się od „Sektorowa”, a pozycja w menu
zaczyna się od „Siatka”. Teraz ten wybór rysuje siatkę sektorową z listwami, bez linii MGRS.

## Do sprawdzenia po zbudowaniu

1. Generator map, arkusz pionowy A3: górne numery siatki są widoczne, nad arkuszem stoją pasek narzędzi i opis arkusza,
   pod nim podpowiedź i współrzędne kursora.
2. Zakładka Siatka → „Opisy przy krawędziach arkusza”: wyłącz „▲ Góra” — numery znikają z góry; włącz „▶ Prawa” —
   pojawiają się po prawej. To samo dla siatki sektorowej (listwy) i WGS84.
3. Przy czterech krawędziach narożniki są puste, a znacznik Północy stoi obok opisów, nie na nich.
4. Zapisz projekt, zamknij, otwórz — wybór krawędzi zostaje. Eksport PDF pokazuje opisy przy tych samych krawędziach.
5. Układ „Siatka Sektorowa (A1, B2, C3...)” — sama siatka sektorowa, bez linii i numerów MGRS.

## Kod

| Plik | Zadanie |
|---|---|
| `Services/MapEditor/GridLabelEdges.cs` | krawędzie (flagi), zapis w projekcie, położenie opisu przy krawędzi, wolne narożniki, miejsce na opisy dla znacznika Północy i legendy |
| `Services/TacticalMapGeneratorView.Engine.cs` | numery MGRS i listwy sektorów przy wybranych krawędziach, przełączniki w zakładce Siatka, zapis i odczyt ustawienia, rozpoznanie siatki sektorowej |
| `Services/TacticalMapGeneratorView.Wgs84.cs` | stopnie WGS84 przy wybranych krawędziach |
| `Services/TacticalMapGeneratorView.cs` | podział podglądu na pasy, arkusz dopasowany do środkowego pasa, domyślne miejsca znacznika Północy, podziałki i legendy |
| `Services/TacticalMapGeneratorView.MapInteraction.cs` | pasek narzędzi i podpowiedź w pasach nad i pod arkuszem |
| `FieldTakHub.Builder.Tests/MapGridLabelEdgesTests.cs` | testy: zapis i odczyt, położenie opisów, wolne narożniki, miejsce na opisy |
