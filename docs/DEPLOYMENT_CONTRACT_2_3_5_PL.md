# Field TAK Hub 2.3.5 — wspólny kontrakt Builder ↔ Android

Wersja rozwojowa 2.3.5 używa `META-INF/fieldtak.json` schema v2 jako źródła prawdy dla Deployment Engine 2.0.

## Polityka wdrożenia

- `atakRequired=true` — ATAK jest wymagany.
- `pluginsRequired=true` — pluginy z paczki są wymagane.
- `serverRequired=true` — jeżeli paczka zawiera host OpenTAKServer, jego diagnostyka jest wymagana.
- `meshtasticRequired=false` — Meshtastic pozostaje opcjonalny, chyba że kontrakt zostanie zmieniony.
- `enrollmentRequired` jest ustawiane na `true`, gdy Builder ma włączony enrollment.
- Mission Package, mapy i overlays są domyślnie opcjonalne.

Android nie zgaduje już znaczenia elementu na podstawie samej obecności pliku — korzysta z pola `deployment`. Starsze paczki bez tego pola zachowują bezpieczne wartości domyślne.
