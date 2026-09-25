#!/usr/bin/env python3
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
main=(ROOT/"apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt").read_text(encoding="utf-8")
vm=(ROOT/"apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt").read_text(encoding="utf-8")

# Diagnostics must remain actionable even before an .ftak package is imported;
# the ViewModel explains the missing package instead of silently ignoring the tap.
assert "FieldPrimaryButton(onClick=vm::runDiagnostics," in main
assert "enabled=state.pkg!=null" not in main[main.find("FieldPrimaryButton(onClick=vm::runDiagnostics"):main.find("FieldPrimaryButton(onClick=vm::runDiagnostics")+220]
assert "vm_diagnostics_package_required" in vm

# Opening ATAK is an app-level action and must not depend on an imported .ftak package.
assert "FieldOutlineButton(onClick=vm::openAtak,modifier=" in main
assert "FieldOutlineButton(onClick=vm::openAtak,enabled=state.pkg!=null" not in main
assert "fun openAtak()" in vm

# Meshtastic check is informational only: it must not silently launch the
# Meshtastic app or hand off the channel. Opening the app is a separate action.
assert "fun testMeshtastic()" in vm
start=vm.index("fun testMeshtastic()")
end=vm.index("  fun openMeshtasticChannel()", start)
block=vm[start:end]
assert "vm_meshtastic_no_channel" in block
assert "vm_meshtastic_test_ready" in block
assert "openMeshtasticApp()" not in block
assert "provisioning.openMeshtasticChannel(url)" not in block
assert "state.updateMessage?.let" in main
print("ANDROID ACTIONS CONTRACT: PASS")
