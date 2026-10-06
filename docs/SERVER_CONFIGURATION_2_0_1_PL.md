# Konfiguracja serwera TAK w Builderze 2.3.0

Field TAK Hub Builder pozwala przygotować profil OpenTAKServer bez ręcznej edycji kodu.

## GUI

W sekcji konfiguracji serwera dostępne są:

- Typ
- Nazwa serwera
- Host / domena
- CoT
- API / Enrollment
- Web

Przykład:

```text
SERVER_TYPE=OpenTAK
SERVER_NAME=OpenTAK Example
HOST=tak.example.org
COT_PORT=8089
API_PORT=8446
WEB_PORT=8443
```

Przed buildem operator może wpisać własny host i porty. Ustawienia są zapisywane w `.fthproj`.

## `server.txt`

Builder może:

- wczytać `server.txt` do pól GUI;
- zapisać aktualną konfigurację;
- wykorzystać ją podczas budowy `.ftak`.

`server.txt` nie powinien zawierać haseł, prywatnych kluczy ani sekretów certyfikatów.

## `server.pref`

Jeżeli `source/atak/` nie zawiera własnego pliku `.pref`, Builder może wygenerować konfigurację `server.pref` na podstawie aktywnego profilu serwera.

Własny plik operatora ma pierwszeństwo przed automatycznie generowanym profilem.
