# Field TAK Hub 2.3.5 — Deployment 2.0 DEV

# Android Deployment 2.0 — Field TAK Hub 2.3.5

## Cel

Field TAK Hub Android 2.3.5 is a development release focused on the original **one QR deployment** workflow.

### Docelowy przebieg

`SCAN → VERIFY → PREFLIGHT → DEPLOY → HANDOFF → VERIFY → READY`

## Co robi Deployment 2.0

1. Odbiera jeden `fieldtak://provision` QR lub HTTPS provisioning link.
2. Pobiera `.ftak` z możliwością wznowienia.
3. Weryfikuje SHA-256, podpis Ed25519 i zaufanie wydawcy.
4. Buduje plan wdrożenia na podstawie zawartości paczki i stanu telefonu.
5. Sprawdza ATAK i wymaganą wersję.
6. Wykrywa brakujące / wymagające aktualizacji pluginy i prowadzi instalację po jednym APK.
7. Wykrywa Meshtastic i prowadzi instalację, jeżeli APK znajduje się w paczce.
8. Po gotowości Meshtastic automatycznie przekazuje kanał `https://meshtastic.org/e/...` do aplikacji Meshtastic.
9. Przekazuje enrollment OpenTAKServer do ATAK.
10. Przekazuje Mission Package / dane mapowe do ATAK jako element opcjonalny.
11. Wykonuje diagnostykę DNS/API/Web/CoT OpenTAKServer.
12. Automatycznie wznawia workflow po powrocie z instalatora Androida, ATAK lub Meshtastic.
13. Nie oznacza telefonu jako gotowego, dopóki wymagane elementy planu nie są zakończone.
14. Elementy opcjonalne mogą pozostać pominięte bez blokowania końcowego statusu.

## Ważne ograniczenia Androida

- Instalacja APK jest kontrolowana przez system Android i może wymagać potwierdzenia użytkownika.
- Field TAK Hub nie może na stockowym Androidzie potwierdzić wewnętrznego, uwierzytelnionego stanu ATAK.
- Enrollment jest przekazywany do ATAK; Hub nie przechowuje tokenu enrollmentu w preferencjach ani logach.
- Mission Package jest przekazywany do ATAK; Hub nie udaje, że ma bezpośredni dostęp do prywatnych katalogów ATAK.
- Konfiguracja kanału Meshtastic jest przekazywana do oficjalnej aplikacji Meshtastic; Hub nie steruje bezpośrednio radiem.

## Plan wdrożenia

Plan jest widoczny w Simple Mode i zawiera osobne pozycje dla:

- ATAK,
- każdego pluginu,
- Meshtastic,
- kanału Meshtastic,
- enrollmentu OTS,
- opcjonalnego Mission Package/map,
- OpenTAKServer.

Każdy element ma stan `READY`, `ACTION_REQUIRED`, `OPTIONAL`, `SKIPPED`, `PROBLEM` lub `UNKNOWN`.
