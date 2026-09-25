from pathlib import Path
root=Path(__file__).resolve().parents[1]
main=(root/"apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt").read_text()
vm=(root/"apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt").read_text()
models=(root/"apps/android/app/src/main/java/org/fieldtak/hub/deployment/DeploymentModels.kt").read_text()
strings=(root/"apps/android/app/src/main/res/values/strings.xml").read_text()
pl=(root/"apps/android/app/src/main/res/values-pl/strings.xml").read_text()
for token in ["MESHTASTIC_CONFIG","StepResult.SKIPPED","skipOptionalStep","skipMeshtasticSetup","isMeshtasticSkipped"]:
    assert token in vm or token in models, token
for token in ["skip_optional_action","skip_enrollment_label","skip_data_package_label","skip_meshtastic_label"]:
    assert token in main or token in strings or token in pl, token
assert "vm::skipMeshtasticSetup" in main
assert "vm::skipOptionalStep" in main
assert "meshSkipped" in main
assert 'item.id=="meshtastic"' in vm
print("ANDROID SIMPLE OPTIONAL SKIP CONTRACT: PASS")
