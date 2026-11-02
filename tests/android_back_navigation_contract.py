#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]
main=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt').read_text()
strings=(root/'apps/android/app/src/main/res/values/strings.xml').read_text()
strings_pl=(root/'apps/android/app/src/main/res/values-pl/strings.xml').read_text()
assert 'BackHandler(enabled=true)' in main
assert 'activity.moveTaskToBack(true)' in main
assert 'lastBackAt' in main
assert 'press_back_again' in main
assert '@Composable\nprivate fun FieldTakScreenContent' in main
assert 'name="press_back_again"' in strings
assert 'name="press_back_again"' in strings_pl
print('ANDROID BACK NAVIGATION CONTRACT: PASS')
