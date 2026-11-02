from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
manager = ROOT / "apps/android/app/src/main/java/org/fieldtak/hub/meshtastic/MeshtasticManager.kt"
vm = ROOT / "apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt"
ui = ROOT / "apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt"

m = manager.read_text(encoding="utf-8")
v = vm.read_text(encoding="utf-8")
u = ui.read_text(encoding="utf-8")

required_manager = [
    "MeshtasticLinkState",
    "MESH_CONNECTED",
    "MESH_DISCONNECTED",
    "CONNECTION_STATE_CHANGED",
    "StateFlow",
    "registerReceiver",
    "CONNECTED",
    "CONNECTING",
    "DISCONNECTED",
]
for token in required_manager:
    assert token in m, f"missing manager bridge token: {token}"

assert "meshtastic.linkState.collect" in v
assert "refreshMeshtasticStatus" in v
assert "meshtastic.close()" in v
assert "onClick=vm::refreshMeshtasticStatus" in u
assert "status_meshtastic_radio_connected" in u
assert "status_meshtastic_radio_connecting" in u
assert "status_meshtastic_radio_disconnected" in u
assert "IMeshService" not in m.split("/**")[0], "Field TAK Hub source must not bind to Meshtastic AIDL"

print("ANDROID MESHTASTIC LINK BRIDGE CONTRACT: PASS")
