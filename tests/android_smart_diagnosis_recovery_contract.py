from pathlib import Path
root=Path(__file__).resolve().parents[1]
ma=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt').read_text()
vm=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt').read_text()
pl=(root/'apps/android/app/src/main/res/values-pl/strings.xml').read_text()
en=(root/'apps/android/app/src/main/res/values/strings.xml').read_text()
for token in ['SMART_DIAGNOSIS','RECOVERY']:
    assert token in ma, token
for token in ['runSmartDiagnosis','recoverCurrentSetup']:
    assert token in vm, token
for token in ['smart_diagnosis','run_smart_diagnosis','recovery','run_recovery','smart_ok','smart_attention']:
    assert f'name="{token}"' in pl, token
    assert f'name="{token}"' in en, token
print('ANDROID SMART DIAGNOSIS / RECOVERY CONTRACT: PASS')
