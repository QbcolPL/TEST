from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

schema = (ROOT / "schemas" / "fieldtak-v2.schema.json").read_text(encoding="utf-8")
model = (ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "Models" / "FieldTakProject.cs").read_text(encoding="utf-8")
ui = (ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "MainWindow.xaml").read_text(encoding="utf-8")
logic = (ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "MainWindow.xaml.cs").read_text(encoding="utf-8")
doc = (ROOT / "docs" / "MESHTASTIC_HYBRID.md").read_text(encoding="utf-8")

assert '"type": {' in schema or '"type"' in schema
assert 'public string Type { get; set; } = "generic_meshtastic";' in model
assert 'Text="generic_meshtastic"' in ui
assert '"generic_meshtastic"' in logic
for example in ["heltec_v3", "tbeam_supreme", "seeed_meshtracker_x1"]:
    assert example in doc, example
builder_readme=(ROOT / "docs" / "README_BUILDER_PL.txt").read_text(encoding="utf-8")
assert "generic_meshtastic" in builder_readme or "hardware" in builder_readme.lower()
print("MESHTASTIC HARDWARE NEUTRALITY CONTRACT: PASS")

# Gateway is optional for generic/field-node profiles and required only for explicit gateway role.
manager = Path(__file__).resolve().parents[1]/'apps/android/app/src/main/java/org/fieldtak/hub/meshtastic/MeshtasticManager.kt'
window = Path(__file__).resolve().parents[1]/'apps/builder/FieldTakHub.Builder/MainWindow.xaml.cs'
assert 'deviceRole' in manager.read_text()
assert 'role == "gateway"' in window.read_text()
