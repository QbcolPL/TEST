# Import paczek TAK Field Hub 1.x do standardu 2.x

Builder 2.3.0 ma funkcję **Importuj ZIP 1.x**.

1. Wskaż oryginalny ZIP z Buildera 1.x.
2. Builder odczytuje `manifest.json`.
3. Jeżeli paczka zawiera `manifest.sig` i `manifest.pub.pem`, weryfikowany jest podpis RSA/SHA-256 dokładnych bajtów manifestu.
4. Weryfikowane są sumy SHA-256 plików i `config.txt`. Błąd integralności przerywa import.
5. Tworzony jest nowy projekt 2.x z katalogami `source/atak`, `plugins`, `maps`, `overlays`, `config`, `data` i `out`.
6. APK ATAK trafia do `source/atak/atak.apk`; pluginy do `source/plugins/`. Profil OpenTAK jest mapowany do pól Buildera 2.x.
7. Builder zapisuje `LEGACY-IMPORT-REPORT.txt` w katalogu projektu.
8. Kliknij **Buduj pakiet**. Nowy `.ftak` jest podpisywany Twoim aktualnym kluczem Publisher Ed25519.

Stary podpis RSA nie jest kopiowany jako podpis 2.x — służy tylko do sprawdzenia autentyczności paczki źródłowej przed migracją.
