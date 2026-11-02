# Field TAK Hub Builder 2.3.0 — UI 2.4 BETA

Builder 2.3.0 zachowuje format `.ftak` i istniejący silnik budowania, ale interfejs został ułożony zgodnie z projektem **Builder 2.4 BETA**. Dzięki temu 2.3 jest bazą funkcjonalną, a UI nie wymaga ponownego projektowania przy przejściu do 2.4.

## Układ interfejsu 2.4

Główny ekran korzysta z układu trzykolumnowego: **nawigacja po lewej → kreator paczki → status i podgląd po prawej**. Kolory, typografia i odstępy są utrzymane w stylistyce Field TAK Hub: prawie czarne tło, ciemne panele, turkusowe akcenty i wysoki kontrast.

W trybie prostym widoczne są:

- **Informacje o wydarzeniu** — nazwa, profil, ważność `.ftak`, ważność QR i opis.
- **Zawartość paczki** — ATAK, pluginy, mapy, overlays, Meshtastic i Server jako osobne kafelki.
- **Dodatkowe opcje** — podpis, notatki i pozostałe ustawienia.
- **Prawy panel** — status, podgląd rozmiaru, wynikowy plik `.ftak` i QR.

Przycisk **Wybierz profil zalecany** ustawia bezpieczne wartości startowe dla profilu MILSIM. Przycisk **Konfiguruj** przenosi Buildera do trybu zaawansowanego i udostępnia odpowiednią sekcję szczegółową.

## Szybka zmiana języka

W prawym górnym rogu znajduje się przełącznik **PL / EN**. Zmiana działa natychmiast, bez otwierania ustawień. UI korzysta z `LocalizationService` oraz plików:

```text
apps/builder/FieldTakHub.Builder/Resources/Strings.pl.xaml
apps/builder/FieldTakHub.Builder/Resources/Strings.en.xaml
```

Przełącznik jest częścią głównego interfejsu, dlatego język można zmienić przed przygotowaniem paczki.

## Tryb prosty / zaawansowany

**Tryb prosty** jest przeznaczony do szybkiego przygotowania paczki dla uczestników wydarzenia. Nie wymaga znajomości struktury katalogów, gatewaya ani szczegółów bezpieczeństwa.

**Tryb zaawansowany** odsłania istniejące funkcje administracyjne: katalogi projektu, Meshtastic HYBRID, Field Node/Gateway, MQTT/TLS, OpenTAKServer, enrollment, zakres ATAK i bezpieczeństwo klucza Publishera.


## UI 2.4 — aktualizacje Full HD

Najnowszy układ Buildera 2.3.0 / UI 2.4 BETA używa przewijania **całej aplikacji** w pionie i poziomie. Dzięki temu tryb zaawansowany nie ma osobnego, zagnieżdżonego scrolla, a na mniejszych ekranach można przewijać cały interfejs razem z lewą nawigacją i prawym panelem.

Ważność paczki `.ftak` i ważność kodu QR są wpisywane bezpośrednio jako **liczba godzin**. Nie ma już ograniczenia do czterech presetów. Przykładowo można wpisać `1`, `6`, `24`, `72` lub dowolną inną dodatnią liczbę całkowitą. Dodatkowe przyciski szybkiego ustawienia pozostają opcjonalnymi skrótami.

Lewe menu jest funkcjonalną nawigacją. Kliknięcie **Server**, **Meshtastic**, **ATAK**, **Certyfikaty TAK Server** lub **Ustawienia** przełącza do trybu zaawansowanego i przewija całą aplikację do właściwej sekcji. **Paczka .FTAK** wraca na początek.

W lewym dolnym rogu znajduje się dekoracyjna grafika terenowa Field TAK Hub, a wszystkie ikony nawigacji są przechowywane jako zasoby projektu w `Assets/navicons/`.

## Funkcje zachowane z Buildera 2.3

- podpisywanie `.ftak`,
- analiza `source/`,
- OpenTAKServer i `server.txt`,
- ATAK Mission Package i pluginy,
- Meshtastic channel/profile,
- Meshtastic Gateway MQTT/TLS,
- enrollment,
- QR / LAN / cloud distribution,
- import legacy 1.x,
- naprawa folderów projektu,
- diagnostyka i walidacja.

## Bezpieczeństwo

Nie umieszczaj w projekcie haseł MQTT, prywatnych kluczy TLS, prywatnego klucza Publishera ani stałych tokenów produkcyjnych. Dane uwierzytelniające gatewaya powinny pozostać poza paczką `.ftak`.

## Lokalna kompilacja

Otwórz rozwiązanie:

```text
apps\builder\FieldTakHub.Builder.sln
```

lub użyj skryptu:

```text
tools\BUILD_WINDOWS_BUILDER_LOCAL_v3.cmd
```

Wymagany jest .NET SDK zgodny z `FieldTakHub.Builder.csproj`. W środowisku CI / lokalnym uruchom także:

```text
python tests/full_static_gate.py
```

Aktualny interfejs posiada kontrakt UI 2.4 sprawdzający podstawowy układ, przełącznik języka, styl dark/turquoise, kafelki zawartości i prawy panel statusu.

## Autor

Jakub /Qbcol/ Baron

Field TAK Hub — connect · deploy · operate

## UI 2.4 — Full HD

Builder 2.3.0 uses the UI 2.4 visual direction: dark tactical palette, cyan Field TAK Hub accents, dedicated vector-style PNG navigation icons, responsive 1600×1000 base window, and a three-column Full HD layout.

### Left navigation

The left menu is functional, not decorative:

- **Paczka .FTAK** — returns to the simple package overview.
- **Server** — opens server/OpenTAKServer configuration in Advanced mode.
- **Meshtastic** — opens Meshtastic + Gateway configuration.
- **ATAK** — opens ATAK version/profile configuration.
- **Certyfikaty TAK Server** — opens publisher/security tools.
- **Ustawienia** — opens Builder settings/update/security tools.

The active section is highlighted with a cyan left marker.

### Icons and branding

Navigation and major package sections use project assets from `Assets/navicons/` so the UI is not dependent on emoji glyphs or a particular Windows font.

### Language

The **PL / EN** selector in the top-right corner switches the Builder language immediately.


## Diagnostyka lokalnej kompilacji / Local build diagnostics

Jeśli zwykły `dotnet publish` kończy się kodem 1, uruchom:

`tools\\BUILD_WINDOWS_BUILDER_LOCAL_DIAGNOSTIC.cmd`

Ten skrypt nie czyści ekranu po błędzie i zapisuje pełne logi do:

`artifacts\\windows\\logs\\restore.log`
`artifacts\\windows\\logs\\build.log`
`artifacts\\windows\\logs\\publish.log`

Dzięki temu pierwszy błąd kompilatora jest zachowany i można go łatwo zgłosić do naprawy.

## UI 2.4 UX defaults

- ATAK Profiles/Settings and TAK Server Certificates are marked as **coming soon** in the navigation and advanced panels.
- `source/meshtastic/channel.url` is optional and no default channel URL is inserted into a new project.
- `source/enrollment/enroll.url` is optional and no default enrollment URI is inserted into a new project.
- The operator must paste their own Meshtastic channel URL or OpenTAK enrollment URI when needed.
