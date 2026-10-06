# One Scan Deployment — instrukcja

1. W Builderze utwórz/otwórz projekt.
2. Skonfiguruj serwer OpenTAKServer.
3. W sekcji **Enrollment OpenTAK / wdrożenie jednym skanem** wklej URI `tak://com.atakmap.app/enroll?...`.
4. Używaj najlepiej tokenu jednorazowego lub krótkotrwałego.
5. Dodaj opcjonalnie ATAK Mission Package, pluginy, mapy i Meshtastic.
6. Zbuduj podpisany `.ftak`.
7. Wygeneruj istniejący provisioning QR/cloud QR.
8. Na telefonie zeskanuj QR w Field TAK Hub tylko raz.

Hub: weryfikuje pakiet → przygotowuje ATAK → przekazuje enrollment do ATAK → kontynuuje pluginy/Mission Package → Meshtastic → diagnostykę OTS.

### Ważne

Jeden skan oznacza jeden skan do **Field TAK Hub**. ATAK może nadal wyświetlić własne ekrany potwierdzenia podczas instalacji/importu. To zależy od wersji Androida i ATAK.
