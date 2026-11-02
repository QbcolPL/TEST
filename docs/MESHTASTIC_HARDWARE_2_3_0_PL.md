# Meshtastic Hardware Neutrality — Field TAK Hub 2.3.0

Field TAK Hub 2.3.0 rozdziela profil sprzętowy od logiki aplikacji.

## Role
- `auto` — brak wymuszenia konkretnej roli urządzenia;
- `tak` — urządzenie terenowe z natywną rolą Meshtastic TAK; w Builderze widoczne jako `FIELD NODE` / `Węzeł terenowy (TAK)`;
- `gateway` — urządzenie infrastrukturalne z wymaganym MQTT/TLS do OpenTAKServer.

## Model
Pole `deviceModel` jest informacyjne. Nie wolno używać go jako mechanizmu wyboru sterownika. Dzięki temu nowe urządzenia Meshtastic mogą działać bez aktualizacji Field TAK Hub, o ile są obsługiwane przez aplikację Meshtastic / plugin.

## Przykładowe wartości
`generic_meshtastic`, `tbeam`, `tbeam_supreme`, `heltec_v3`, `heltec_v4`, `seeed_meshtracker_x1`, `seeed_xiao_esp32s3`, `seeed_xiao_nrf52840`, `rak4631`, `other`.

Lista nie jest zamknięta. Builder pozwala również wpisać własny identyfikator modelu.
