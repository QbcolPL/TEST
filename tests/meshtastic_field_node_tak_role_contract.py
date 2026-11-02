from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
XAML = (ROOT / "apps/builder/FieldTakHub.Builder/MainWindow.xaml").read_text(encoding="utf-8")
CS = (ROOT / "apps/builder/FieldTakHub.Builder/MainWindow.xaml.cs").read_text(encoding="utf-8")
PS = (ROOT / "apps/builder/FieldTakHub.Builder/Services/ProjectService.cs").read_text(encoding="utf-8")
ANDROID = (ROOT / "apps/android/app/src/main/java/org/fieldtak/hub/diagnostics/ServerDiagnostics.kt").read_text(encoding="utf-8")

assert 'Content="Field Node" Tag="tak"' in XAML
assert '"tak" or "field_node" => 1' in CS
assert 'role == "tak" || role == "field_node"' in CS
assert 'p.Meshtastic.DeviceRole = "tak"' in PS
assert '(meshtastic.deviceRole=="tak" || meshtastic.deviceRole=="field_node")' in ANDROID

print("MESHTASTIC FIELD NODE -> TAK CONTRACT: PASS")
