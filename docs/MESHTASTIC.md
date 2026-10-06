# Meshtastic w Field TAK Hub 2.1

Field TAK Hub 2.1 obsługuje kanoniczne linki kanałów Meshtastic:

`https://meshtastic.org/e/#<ChannelSet>`

## OpenTAKServer

1. W OpenTAKServer Web UI otwórz konfigurację Meshtastic.
2. Utwórz kanał i włącz `downlink`, jeżeli ma przyjmować wiadomości z OTS.
3. Użyj wygenerowanego przez OTS kodu QR kanału.

## Bezpośredni QR

W Field TAK Hub wybierz **STAN URZĄDZENIA → SKANUJ QR / LINK**. Hub rozpoznaje URL Meshtastic i przekazuje go do oficjalnej aplikacji Meshtastic.

## Pakiet .ftak

Aby dołączyć kanał do pakietu przygotowywanego przez Builder:

1. Otwórz `source/meshtastic/`.
2. Utwórz plik `channel.url`.
3. Wklej jedną linię z kanonicznym URL-em `https://meshtastic.org/e/#...`.
4. Zbuduj pakiet `.ftak`.

Builder umieszcza ten plik w `payload/meshtastic/channel.url` i oznacza pakiet jako zawierający konfigurację Meshtastic. Field TAK Hub po weryfikacji podpisu pokazuje ją na ekranie **Stan urządzenia**. Dotknięcie karty Meshtastic otwiera konfigurację w aplikacji Meshtastic.

## Ważne

URL kanału zawiera dane umożliwiające dołączenie do kanału, więc traktuj go jak sekret dystrybucyjny. Nie zapisuj go w publicznym repozytorium projektu.

Field TAK Hub nie przejmuje bezpośredniej kontroli nad radiem Meshtastic. Nie deklaruje też połączenia z radiem tylko dlatego, że aplikacja Meshtastic jest zainstalowana.
