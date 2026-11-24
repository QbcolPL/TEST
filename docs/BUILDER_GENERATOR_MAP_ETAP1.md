# Generator map: praca na mapie (etap 1 uproszczenia interfejsu)

Narzędzia rysowania przeniesione z panelu na mapę. Panel boczny został bez przebudowy: zamiast sześciu dużych
przycisków narzędzi w sekcji 3 jest krótka podpowiedź.

## Pasek narzędzi nad mapą

| Ikona | Narzędzie | Klawisz |
|---|---|---|
| ✋ | Zaznacz / przesuń mapę | V |
| ➤ | Znacznik NATO (symbol z listy w sekcji 3 albo z pierścienia) | N |
| ● | Kropka | K |
| 〰 | Trasa / linia | T |
| ⬠ | Sektor / strefa | S |
| 🛡 | Teren gry (AO) | A |
| 📏 | Pomiar odległości i azymutu | L |
| ? | Ściąga skrótów | — |

Ponowne kliknięcie aktywnego narzędzia (albo ponowne naciśnięcie jego litery) wraca do zaznaczania.
Na dole mapy jest podpowiedź: co robi aktywne narzędzie i jak je zakończyć.

## Prawy przycisk myszy

- **Puste miejsce mapy** — pierścień „postaw tutaj” z 8 pozycjami: znacznik NATO (z wyborem symbolu), kropka,
  trasa, sektor, teren gry (AO), sektor ognia, pomiar, kopiuj MGRS. Można kliknąć i wybrać pozycję albo
  przytrzymać prawy przycisk, przeciągnąć w stronę pozycji i puścić.
- **Znacznik, kropka, uchwyt ✥ kształtu albo linia trasy / krawędź obszaru** — menu obiektu: zmień nazwę, kolor,
  typ (AO / sektor / trasa), duplikuj, uprość kształt, kopiuj współrzędne, pokaż na liście, usuń.
  Wnętrze obszaru nie otwiera menu AO — tam prawy klik nadal otwiera pierścień.
- **W trakcie rysowania kształtu** — kończy kształt (jak wcześniej).
- **Wierzchołek kształtu** — usuwa wierzchołek (jak wcześniej).
- **Sektor ognia** — obracanie przeciąganiem (jak wcześniej).

Wcześniej prawy klik na pustej mapie uruchamiał ukrytą linijkę — teraz to pozycja „Pomiar” w pierścieniu
i narzędzie 📏 (L). Pomiar pokazuje odległość i azymut z kierunkiem, np. „245 m · azymut 63° PN-WSCH”,
a linia idzie za kursorem do drugiego kliknięcia.

## Klawiatura

| Klawisz | Działanie |
|---|---|
| Enter | zakończ rysowany kształt |
| Esc | po kolei: zamknij menu, zakończ kształt, wyłącz narzędzie, wyczyść pomiar, odznacz obiekt; w polu tekstowym — wróć do mapy |
| Del | usuń zaznaczony obiekt |
| Ctrl+D | duplikuj zaznaczony obiekt |
| F2 | zmień nazwę zaznaczonego obiektu |
| F | pokaż zaznaczony obiekt (wyśrodkuj mapę) |
| Shift | kąt co 45° przy rysowaniu; kilka znaczników / kropek naraz |

Skróty nie działają podczas pisania w polach tekstowych i na rozwiniętych listach.

## Zmiany zachowania

- Narzędzia działają raz: po postawieniu znacznika albo kropki i po zakończeniu kształtu edytor wraca do
  zaznaczania (Shift — kilka znaczników naraz).
- Kształt z za małą liczbą punktów (np. AO z dwóch kliknięć, trasa z jednego) nie jest dodawany.
- Po dodaniu obiektu fokus zostaje na mapie (działają skróty), a lista przewija się do nowej karty.
  Nazwę zmienia się klawiszem F2 albo z menu obiektu.
- Kliknięcie w puste miejsce mapy odznacza obiekt; kursor nad mapą pokazuje tryb (strzałka — zaznaczanie,
  krzyżyk — rysowanie i pomiar).

## Kod

| Plik | Zadanie |
|---|---|
| `Services/TacticalMapGeneratorView.MapInteraction.cs` | pasek narzędzi, pierścień, menu obiektu, skróty, podpowiedzi, pomiar |
| `Services/MapEditor/MapRadialMenu.cs` | rysowanie pierścienia i podświetlenie pozycji |
| `Services/MapEditor/MapInteractionGeometry.cs` | geometria pierścienia, trafianie w linie kształtów, opis pomiaru (testy: `MapInteractionTests`) |
| `Services/MapEditor/MapEditorUi.cs` | wspólne kontrolki: przyciski paska, pozycje menu, panele pływające |
