# Audyt kodu Buildera Windows — Field TAK Hub

| | |
|---|---|
| Data | 2026-11-24 |
| Commit bazowy | `2aefcda` (gałąź `main`) |
| Zakres | `apps/builder` — 36 plików C# (27 038 linii) i 6 plików XAML (1 156 linii) |
| Metoda | analiza statyczna: lektura kodu, wyszukiwanie wzorców, skan nieużywanych metod, metryki |
| Kompilacja | lokalnie niemożliwa (brak .NET SDK w środowisku audytu) — poprawki kompiluje i testuje GitHub Actions (`windows-builder.yml`) |

Numery linii dotyczą plików z commita `2aefcda`, czyli stanu przed poprawkami.

## Najważniejsze w skrócie

1. **Klucz szyfrowania kanału wychodził z komputera.** Tryb „QR Offline (JSON)” wysyłał cały JSON strony — razem z kluczem PSK Meshtastic i linkiem kanału — w adresie URL do zewnętrznej usługi `api.qrserver.com`. Bez internetu kod QR w ogóle się nie pojawiał. **Naprawione.**
2. **Serwer paczek w sieci LAN oddawał cudze paczki.** Gdy adres nie pasował do żadnej strony, serwer `:8766` wysyłał pierwszą dostępną paczkę `.ftak` — każdy w tej samej sieci Wi-Fi mógł pobrać paczkę (z PSK) wpisując dowolny adres. **Naprawione.**
3. **OPORD przez Wi-Fi/LAN nie mógł działać.** Treść rozkazu była budowana w wątku serwera, a kod czyta kontrolki WPF, co WPF pozwala robić tylko w wątku interfejsu. Wyjątek połykał pusty `catch`, więc telefon dostawał zerwane połączenie. **Naprawione.**
4. **Programator USB raportował sukces bez sprawdzenia.** Wynik zapisu do portu był ignorowany, brak odpowiedzi płytki nie przerywał pracy, a niepoprawny klucz PSK był po cichu zastępowany losowym — płytka trafiała na kanał, którego klucza nie znał nikt w drużynie. **Naprawione** (z jednym ograniczeniem opisanym niżej).
5. **Atrapy kodów QR.** Plakat A4 v2.4, plik `QR_*.png` i okno wsparcia twórcy rysowały wzór z hasha SHA-256 udający kod QR — nie da się go zeskanować. **Naprawione** — są prawdziwe kody QR generowane lokalnie.
6. **Spaghetti.** Kod ma trzy formaty paczek, trzy serwery HTTP, dwanaście warstw „łatek” i 185 pustych `catch`. Tego nie da się bezpiecznie naprawić jedną zmianą — poniżej jest plan refaktoryzacji krok po kroku.

Legenda statusu: ✅ naprawione w tej paczce · 📋 do refaktoryzacji (plan w sekcji 7) · ⚠ wymaga Twojej decyzji.

## 1. Placeholdery (zaślepki)

| # | Gdzie | Co jest nie tak | Status |
|---|---|---|---|
| P1 | `MainWindow.xaml:501`, `Resources/Strings.*.xaml:141` | „Funkcje profili i ustawień ATAK będą dostępne wkrótce”. Panel naprawdę ustawia zakres wersji ATAK zapisywany w manifeście paczki v2 (`FtakPackageBuilder.cs:137`). | ✅ opis zgodny z tym, co panel robi |
| P2 | `MainWindow.xaml:590`, `Resources/Strings.*.xaml:142–143` | „Generowanie certyfikatów TAK Server będzie dostępne wkrótce”. Panel zawiera klucz podpisu paczek Ed25519 (odcisk, eksport, import). | ✅ nazwa „Klucz podpisu paczek” i prawdziwy opis |
| P3 | `Services/TacticalConfigPanel.cs:2198` (`DrawDeterministicQrMatrix`), wywołania `:1918`, `:2118` | Atrapa QR na plakacie A4 v2.4 i w `QR_*.png`: trzy „oczka” plus bity z SHA-256. Nieskanowalna — dotyczy też linku enrollmentu ATAK, jeśli strona go ma. | ✅ prawdziwy kod (`QrService.DrawQr`) |
| P4 | `Services/GeoportalLidarEngine.cs:914` (`RenderFallbackQrBitmap`) | Atrapa QR w oknie wsparcia twórcy; prawdziwy kod przychodził z internetu, offline zostawała atrapa. | ✅ prawdziwy kod lokalnie |
| P5 | `MainWindow.xaml.cs:99–100`, `Services/BuilderCoreSidebarAndProjectEngine.cs:2633–2634` | Puste metody: `SyncNavigationServerFields`, `SyncNavigationAtakFields` (wywoływane 3×, nic nie robią), `AttachToMainWindow`, `ApplyAllStep1Fixes`. | 📋 usunąć |
| P6 | `tests/source_gate.py` | „Bramka źródeł” w CI zawsze wypisywała PASS i niczego nie sprawdzała. | ✅ prawdziwe reguły (sekcja 5) |
| P7 | `BuilderCoreSidebarAndProjectEngine.cs:2397–2398`, `BuilderLocalizationAndOpordEngine.cs:635, 978–979, 1056–1057, 1436–1437`, `BuilderLocalizationAndOpordEngine.UiPart.cs:619–620` | Zaszyty punkt 49.8494°N 18.8127°E jako domyślna lokalizacja AO, dni OPORD i prognozy METOC. Gdy użytkownik nie wskaże obszaru, rozkaz i pogoda dotyczą tego miejsca. | ⚠ proponuję „brak lokalizacji” i wymóg wskazania AO |
| P8 | `BuilderUiSimplifier.cs:757–774`, `BuilderLocalizationAndOpordEngine.cs:1722–1738`, `DistributionServer.cs:20, 191–195` | Trzy kopie wyboru adresu IP; przy braku sieci zwracały `127.0.0.1`, który trafiał do kodu QR (telefon pod nim nie znajdzie komputera). Pierwsza kopia brała też pierwszy adres `172.*`/`192.168.*` — często kartę Hyper-V, WSL albo VirtualBox. | ✅ wspólny `LanAddressHelper` + ostrzeżenie w oknach QR |
| P9 | `TacticalConfigPanel.cs:1856` | QR na plakacie v2.4 (`ftak://provision?…&sha256=<16 znaków>`) nie zawiera adresu pobrania — telefon nie ma skąd wziąć paczki. Nowa aplikacja Android rozpoznaje ten kod i prosi o import pliku. | 📋 docelowo QR z adresem LAN lub chmury |

## 2. Ślepe funkcje (wyglądają na działające, a nie działają)

### S1 — Krytyczne — PSK w adresie URL do zewnętrznej usługi ✅

- `BuilderUiSimplifier.cs:787–821` buduje „offline JSON” strony z `Psk` (`:803`) i `ChannelUrl` (`:804`), a `:966` wysyła go jako parametr `data=` do `https://api.qrserver.com/…`. To samo dla adresu OPORD (`BuilderLocalizationAndOpordEngine.UiPart.cs:1482`) i linku wsparcia (`GeoportalLidarEngine.cs:773`).
- Skutek: klucz szyfrowania kanału trafia do obcego serwera (logi, pośrednicy, pamięć podręczna na dysku `QR_V3_…`). Bez internetu nie ma kodu.
- Poprawka: wszystkie kody QR powstają lokalnie w `QrService` (biblioteka QRCoder, już używana przez projekt). Długie treści automatycznie schodzą z korekcji Q na M lub L, a gdy nic się nie mieści, okno pokazuje czytelny komunikat zamiast pustego pola.

### S2 — Krytyczne — serwer `:8766` wydawał paczkę dowolnej strony ✅

- `BuilderUiSimplifier.cs:723–735`: gdy adres nie pasuje, `targetFile = ServedFtakPathsByFaction.Values.FirstOrDefault(File.Exists)` — czyli pierwsza paczka z brzegu, z PSK tej strony. Wystarczyło `GET /`.
- `:695–702` i `:724–731`: dopasowanie przez „zawiera” — przy stronach `RED` i `REDFOR` adres jednej mógł zwrócić drugą.
- Słownik ścieżek był czytany z wątków serwera i zapisywany z wątku interfejsu bez synchronizacji.
- Poprawka: dokładne dopasowanie `/deploy/<Strona>.ftak` i `/opord/<Strona>.html|.json`, odpowiedź `404` w każdym innym przypadku, `ConcurrentDictionary`.

### S3 — Wysokie — OPORD przez LAN kończył się zerwanym połączeniem ✅

- Serwery `:8766` (`BuilderUiSimplifier.cs:707`) i `:8769` (`BuilderLocalizationAndOpordEngine.cs:1703`) wywoływały `GeneratePrintableOpordHtml` w wątku serwera. Ta metoda czyta kontrolki WPF — np. `GetEventNameFromWindow` sięga po `TextBox.Text` (`TacticalConfigPanel.cs:1938`) — a WPF rzuca `InvalidOperationException` przy dostępie z innego wątku. Pusty `catch` zamieniał to w zerwane połączenie.
- Dodatkowo serwer `:8769` przy nieznanej stronie wydawał OPORD aktywnej strony, a `GetOrCreateModel` modyfikował słownik modeli z wątku serwera.
- Poprawka: `OpordBuilderManager.BuildOpordLanPayloadOnUiThread` buduje treść w wątku interfejsu (Dispatcher); wybór strony też odbywa się w wątku interfejsu; nieznana strona daje `404`.

### S4 — Wysokie — programator USB Meshtastic ✅ (częściowo 📋)

`Services/MeshtasticUsbProvisionerView.cs`:

- `:243–251` — niepoprawny Base64 klucza → losowe 32 bajty bez słowa. Pusty klucz przechodził jako kanał **bez szyfrowania**.
- `:413–416` — wynik `WriteFile` ignorowany (`out uint _`), więc wypięty kabel też kończył się „sukcesem”.
- `:427–429` — brak odpowiedzi płytki → numer węzła 0 i dalsze wysyłanie poleceń administracyjnych, których płytka pod tym adresem nie zastosuje.
- `:456–457` — zawsze „✓ SUKCES! Płytka … zaprogramowana”; w historii „✓ WGRANO” (`:701`), numer seryjny rośnie dalej.

Poprawka: klucz sprawdzany **przed** otwarciem portu (16 B lub 32 B, poprawny Base64), każdy zapis musi przejść w całości, brak odpowiedzi albo nieodczytany numer węzła przerywa pracę z jasnym komunikatem „nic nie zostało zapisane”, a końcowy komunikat mówi prawdę: „wysłano konfigurację — potwierdź w aplikacji Meshtastic”, status „✓ WYSŁANO”.

Ograniczenie (📋): ta ścieżka nadal nie odczytuje potwierdzeń z płytki (pakietów `FromRadio`/Routing ACK), więc nie wiadomo na pewno, czy płytka zapisała ustawienia. Pełna weryfikacja wymaga odczytu odpowiedzi protobuf i testu na prawdziwym sprzęcie.

### S5 — Średnie — martwa refleksja w mapie ✅

`BuilderCoreSidebarAndProjectEngine.cs:2432–2459` szukał przez refleksję metod `OpordBuilderManager.GetExtractedMapData` i `ExtractMapData`, których nie ma w kodzie — gałąź nigdy się nie wykonywała. Usunięta, zachowanie bez zmian.

### S6 — Średnie — wczytywanie projektu „na ślepo” 📋

- `BuilderCoreSidebarAndProjectEngine.cs:746–786` wywołuje przez refleksję **każdą** statyczną metodę `TacticalConfigPanel`, której nazwa zawiera „Load”, „Restore”, „Import” lub „Apply” (także prywatne), w nieokreślonej kolejności, z połykaniem błędów. Dziś trafia m.in. w `ApplyFullWorkspaceStateJson`, `LoadSessionStateAuto` i `ApplyIndefiniteValidityBeforeAction`. Każda nowa metoda o takiej nazwie zostanie wywołana przy otwarciu projektu.
- `:719–733` nadpisuje treścią otwieranego projektu **każdy** plik `*.json` w `%LOCALAPPDATA%\FieldTakHub` (poza `opord_*`), żeby wszystkie ścieżki wczytywania „trafiły” na ten sam stan.
- Nie zmieniałem tego w tej paczce: bez uruchomienia aplikacji ryzyko regresji przy otwieraniu projektów jest zbyt duże. Docelowo: jedno jawne wywołanie `ApplyFullWorkspaceStateJson` i jeden plik sesji.

### S7 — Niskie — inne

- `BuilderCoreSidebarAndProjectEngine.cs:528–538` ustawia przez refleksję pole, które i tak jest publiczne. 📋
- 185 z 263 bloków `catch` jest pustych — błędy znikają bez śladu (tak ukrywał się S3). 📋
- 17 metod nie jest wywoływanych nigdzie, także w testach: `MainWindow.xaml.cs` — `Analyze_Click`, `UpdateGeneratedPreview`, `SaveProject_Click`, `LoadProject_Click`, `OpenProjectFolder_Click`, `WriteMeshtasticProfile`; `BuilderCoreSidebarAndProjectEngine.cs` — `TryInvokeOriginalButtonByText`, `ApplySidebarAndHeaderFixes`, `EnsureFactionOutputDir`, `ApplyAllStep1Fixes`, `CollectAllNodes`; `BuilderLocalizationAndOpordEngine.cs` — `DrawRandomWord`, `ValidateAndPrepareMetocBeforeFetch`; `BuilderUiSimplifier.cs` — `ShowOpordView`; `SourceFileFilter.cs` — oba `GetFilteredFiles`; `TacticalMapGeneratorView.RangeSectors.cs` — `InjectRangeSectorsIntoKmlDocument`. 📋

## 3. Spaghetti i architektura

| Obszar | Stan |
|---|---|
| Wielkość plików | `TacticalMapGeneratorView` 9 033 linie w 4 plikach `partial`; OPORD 5 081 (`UiPart` 2 866 + silnik 2 215); `BuilderCoreSidebarAndProjectEngine` 2 639; `TacticalConfigPanel` 2 549; `GeoportalLidarEngine` 1 821; `BuilderUiSimplifier` 1 729 |
| Interfejs w kodzie | ok. 655 miejsc tworzących kontrolki (`new Button`, `new Grid`…) przy 6 plikach XAML; brak MVVM i bindingów |
| „Chirurgia” drzewa UI | 62 odwołania do `VisualTreeHelper`/`LogicalTreeHelper`; metody `CollapseObsoleteBuilderModeSelector`, `RemoveDuplicateInnerSidebars`, `CaptureAndCollapseTopMenuBar`, `CollapseLegacyLeftSidebarColumn` chowają stary interfejs zamiast go usunąć |
| Szukanie kontrolek po tekście | np. nazwa wydarzenia to TextBox obok TextBlocka „Nazwa wydarzenia” (`TacticalConfigPanel.cs:1936`); układ współrzędnych brany z **dowolnego** ComboBoxa w oknie, który zawiera „MGRS”/„UTM” (`BuilderCoreSidebarAndProjectEngine.cs:2423–2430`) — zmiana etykiety psuje logikę |
| Warstwy łatek | 12 klas: `V34CleanAnnex3AndDiscordEngine`, `V34CleanFtakAndFactionEngine`, `V34CleanMapStudioBridge`, `V34CleanOpordCommsStore`, `V34CleanOpordDataEngine`, `V34CleanOpordUiTransformer`, `V34CleanSidebarAndHeaderEngine`, `V34CleanUsbProgrammerBridge`, `V34Step1ShellAndSidebarFixer`, `V34FactionSnapshot`, `V34FactionVoiceChannelRow`, `BuilderUiSimplifier` |
| Stan globalny | ok. 214 pól statycznych; widoki WPF leżą w folderze `Services` |
| Duplikaty modeli | strona: `FactionProfileModel`, `CleanFactionProfileModel`, `V34FactionSnapshot`; kanał głosowy: `VoiceChannelModel`, `CleanVoiceChannelItem`, `V34FactionVoiceChannelRow`, `OpordManualChannelItem` |
| Duplikaty pomocników | wybór IP ×3 (✅ ujednolicone), `GetAllVisualDescendants` ×2, `IsDescendantOf` ×2, `SanitizeFileName` ×2 |
| Formaty paczek | trzy formaty i trzy silniki zapisu: v2 podpisany Ed25519 (`FtakPackageBuilder.cs:148–175`, `META-INF/fieldtak.json`), v2.4 (`TacticalConfigPanel.cs:2054–2092`, `manifest.json` + `source/`), v3.0 (`BuilderUiSimplifier.cs:463–466`, `config.json` + `opord.json`/`opord.html`) |
| Serwery HTTP | trzy: `:8765` v2 (token, ważność, limit pobrań — najlepszy), `:8766` v3.0 i `:8769` OPORD (ręczny HTTP na `TcpListener`, bez uwierzytelnienia) |
| Inne | P/Invoke `kernel32` zamiast `System.IO.Ports`; 3 × `async void`; 3 osobne `HttpClient`, jeden z podrobionym User-Agentem Chrome (`GeoportalLidarEngine.cs:1112`); numery wersji rozjechane: projekt 2.3.0, User-Agent 2.2.0-rc1, schemat 3.0, teksty „2.4”, klasy „V34” |
| Testy | 22 testy, głównie silnik v2 i drobne funkcje; zero testów v3.0, serwerów LAN, USB i OPORD. ✅ dodano 6 testów do poprawek z audytu |

## 4. Usługi zewnętrzne, prywatność, licencje

| Usługa | Uwagi | Status |
|---|---|---|
| `api.qrserver.com` | wysyłała treść kodów QR, w tym PSK | ✅ usunięta |
| kafelki Google (`mt1.google.com`, `TacticalMapGeneratorView.Engine.cs:231–233`) | bezpośrednie pobieranie kafelków poza oficjalnym API jest sprzeczne z warunkami Google | ⚠ usunąć albo przejść na oficjalne API |
| `tile.openstreetmap.org`, Nominatim | polityka OSMF nie pozwala na masowe pobieranie (mapy offline) i wymaga User-Agenta identyfikującego aplikację — tu klient udaje przeglądarkę | ⚠ |
| ArcGIS Online (`server/services.arcgisonline.com`, `ibasemaps-api.arcgis.com`) | wymagania Esri co do konta i atrybucji | ⚠ sprawdzić warunki |
| Geoportal (`mapy.geoportal.gov.pl`), open-meteo | usługi publiczne/darmowe z warunkami (atrybucja, użycie niekomercyjne) | ⚠ dopisać atrybucję |
| buycoffee.to | link wsparcia twórcy, kod QR teraz lokalny | ✅ |
| Discord | tylko kopiowanie Markdown do schowka, bez połączeń sieciowych | ✓ bez uwag |
| GitHub API | sprawdzanie aktualizacji | ✓ bez uwag |

Warunki usług się zmieniają — przed wydaniem publicznym sprawdź aktualne zasady każdego dostawcy map.

## 5. Co zmienia ta paczka

Poprawki leżą w `_fth_patch/files` i nakłada je `FTH_BUILD_ALL_IN_ONE.bat` (skrypt `_fth_patch/apply-builder-fixes.ps1`): **wszystkie albo żadna**, i tylko gdy każdy plik jest w wersji z commita `2aefcda` (porównanie SHA-256 po ujednoliceniu końców linii). Jeśli którykolwiek plik zmienił się od audytu, kod Buildera zostaje nietknięty, a skrypt wypisuje listę różnic.

| Plik | Zmiana |
|---|---|
| `Services/QrService.cs` | lokalne kody QR: `DrawQr` (plakaty), `CreateImageSource` (okna), `CreateDataFitting` (dobór korekcji Q→M→L) |
| `Services/LanNetwork.cs` *(nowy)* | `LanAddressHelper` (hotspot Windows 192.168.137.1 → Wi-Fi z bramą → Ethernet; pomija karty wirtualne i VPN; ostrzeżenie zamiast cichego 127.0.0.1) i `LanHttp` (dokładny routing, odpowiedź 404) |
| `Services/BuilderUiSimplifier.cs` | serwer `:8766` bez wycieku cudzych paczek, OPORD w wątku UI, lokalny QR, ostrzeżenie o braku sieci |
| `Services/BuilderLocalizationAndOpordEngine.cs` | serwer `:8769`: dokładne dopasowanie strony, budowa OPORD w wątku UI, wspólny wybór IP |
| `Services/BuilderLocalizationAndOpordEngine.UiPart.cs` | lokalny QR OPORD, ostrzeżenie o braku sieci |
| `Services/GeoportalLidarEngine.cs` | prawdziwy QR w oknie wsparcia, usunięta atrapa |
| `Services/TacticalConfigPanel.cs` | prawdziwy QR na plakacie A4 i w `QR_*.png`, usunięta atrapa |
| `Services/MeshtasticUsbProvisionerView.cs` | walidacja PSK przed zapisem, kontrola zapisu, przerwanie bez odpowiedzi płytki, uczciwe komunikaty |
| `Services/BuilderCoreSidebarAndProjectEngine.cs` | usunięta martwa refleksja |
| `Services/DistributionServer.cs` | wspólny wybór IP |
| `MainWindow.xaml`, `Resources/Strings.pl.xaml`, `Resources/Strings.en.xaml` | zamiast „wkrótce” — opis tego, co panele naprawdę robią |
| `FieldTakHub.Builder.Tests/AuditFixesTests.cs` *(nowy)* | testy: adresy prywatne, ostrzeżenie 127.0.0.1, routing serwera, walidacja PSK, QR dla długich treści |
| `tests/source_gate.py` | prawdziwa bramka CI: blokuje zewnętrzny generator QR, atrapy QR, napisy „wkrótce”/„coming soon” i znaczniki konfliktów scalania w `apps/builder` i `apps/android-hub` |

Kontrola przed wydaniem: bilans nawiasów we wszystkich zmienionych plikach C#, poprawność XML plików XAML, bramka źródeł uruchomiona na drzewie z poprawkami (PASS) i na drzewie sprzed poprawek (FAIL — 14 trafień), symulacja nakładania poprawek (pliki z CRLF, LF, mieszane, ponowne uruchomienie, plik zmieniony przez użytkownika).

## 6. Jak wdrożyć

1. Rozpakuj archiwum do folderu repozytorium `C:\Users\Qbcol\Desktop\TEST-main` i zgódź się na zastąpienie plików.
2. Uruchom `FTH_BUILD_ALL_IN_ONE.bat` (dwuklik).
3. Skrypt nałoży poprawki Buildera, pokaże listę plików do commita i zapyta „T/N”. Po „T” wyśle zmiany (bez `--force`), uruchomi oba buildy w GitHub Actions i poczeka na wynik.
4. Wyniki trafią do `%USERPROFILE%\FieldTakHub-builds`: `run-<id>` — APK, `builder-run-<id>` — `FieldTakHub.Builder.exe`. Przy podłączonym telefonie skrypt zaproponuje `adb install`.
5. Gdy któryś build się nie uda, log jest w `build-failed.log` w tym samym folderze — prześlij go, poprawię kod.

## 7. Plan refaktoryzacji (kolejność)

1. **Jeden format `.ftak`**: v3.0 jako treść + podpis Ed25519 z v2 + adres pobrania w kodzie QR. Jeden silnik zapisu i jeden serwer HTTP z tokenem, ważnością i limitem pobrań (wzorem `DistributionServer`).
2. **Jeden model strony i kanału** z jawnym mapowaniem do JSON; testy „Builder → plik → aplikacja Android” na wspólnych próbkach w `examples/`.
3. **Koniec z warstwami `V34*`/`*Fixer`**: najpierw `x:Name` i binding zamiast szukania kontrolek po tekście, potem widoki z `Services` do `Views` (XAML + ViewModel), na końcu usunięcie kodu, który chowa stary interfejs.
4. **Błędy widoczne**: puste `catch` zamienić na log (`%LOCALAPPDATA%\FieldTakHub\logs`) i komunikat dla użytkownika.
5. **USB**: `System.IO.Ports`, odczyt odpowiedzi `FromRadio` z potwierdzeniem zapisu, test na prawdziwej płytce.
6. **Mapy offline** tylko ze źródeł, które na to pozwalają (Geoportal, własne MBTiles), z atrybucją.
7. **Sprzątanie**: 17 nieużywanych metod, 4 puste zaślepki, jedno źródło numeru wersji.

## 8. Ograniczenia audytu

- Analiza statyczna — kod nie był uruchamiany ani kompilowany lokalnie. Pierwszą pełną kompilację i testy wykona GitHub Actions.
- Nie testowano na sprzęcie (płytki Meshtastic przez USB, telefony w sieci Wi-Fi).
- Wnioski o zachowaniu w czasie działania (np. S3) wynikają z lektury kodu i znanych zasad WPF, nie z pomiaru.
