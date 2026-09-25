# Field TAK Hub 2.3.0 — instrukcja testów

## Cel

Celem testów jest potwierdzenie działania aplikacji Android, Windows Buildera oraz kompletnego przepływu ATAK / OpenTAKServer / Meshtastic.

## 1. Test aplikacji Android

1. Uruchom Field TAK Hub.
2. Sprawdź przełączanie PL / EN.
3. W Simple Mode sprawdź:
   - Przygotuj telefon,
   - Importuj `.ftak`,
   - Stan urządzenia,
   - Sprawdź wszystko,
   - Skonfiguruj radio, gdy paczka zawiera konfigurację Meshtastic.
4. Przejdź do Advanced Mode.
5. Sprawdź osobne sekcje OpenTAKServer:
   - Enrollment użytkownika z QR,
   - Paczki danych z QR.
6. Sprawdź diagnostykę i historię.
7. Sprawdź panel wsparcia autora i otwarcie BuyCoffee.

## 2. Nawigacja Wstecz

- Wewnątrz aplikacji pierwsze naciśnięcie wraca do poprzedniego ekranu.
- Na ekranie głównym pierwsze naciśnięcie nie zamyka aplikacji.
- Drugie szybkie naciśnięcie w ciągu 2 sekund przenosi aplikację do tła.

## 3. `.ftak`

Sprawdź kolejno:

- import lokalnego `.ftak`;
- poprawność podpisu;
- wygasłą paczkę;
- brak wymaganych plików;
- poprawne przekazanie danych do ATAK.

## 4. QR

Przetestuj wszystkie trzy podstawowe typy:

- provisioning telefonu;
- enrollment użytkownika;
- Data Package.

W przypadku złego typu QR aplikacja powinna odrzucić workflow i pokazać czytelny komunikat.

## 5. Meshtastic

Sprawdź co najmniej:

- brak aplikacji Meshtastic;
- aplikacja zainstalowana bez przygotowanego kanału;
- paczka z `channel.url`;
- paczka z profilem Meshtastic;
- radio połączone z telefonem;
- radio odłączone;
- przejście do oficjalnej aplikacji/pluginu Meshtastic.

Minimalna matryca sprzętowa:

- T-Beam Supreme;
- SenseCAP MeshTracker X1;
- Heltec jako gateway infrastrukturalny.

## 6. Test end-to-end

```text
Telefon
  -> Radio
  -> LoRa
  -> Gateway
  -> MQTT/TLS
  -> OpenTAKServer
  -> ATAK
```

Sprawdź PLI i GeoChat oraz potwierdź, że komunikacja działa w rzeczywistym środowisku radiowym.

## 7. Raport błędu

Do raportu dołącz:

- wersję aplikacji;
- model telefonu;
- wersję Androida;
- model radia Meshtastic;
- wersję ATAK / pluginów;
- opis kroków odtwarzających;
- ekran diagnostyki, jeśli jest dostępny;
- log GitHub Actions, jeżeli problem dotyczy kompilacji.

Nie dołączaj haseł, kluczy, tokenów, prywatnych Channel URL ani sekretów enrollmentu.
