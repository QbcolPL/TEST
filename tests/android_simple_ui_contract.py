#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]
main=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt').read_text()
vm=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt').read_text()
for token in ['SimpleReadinessBanner','configureMeshtastic','simple_ready_title','simple_radio_action_title']:
    assert token in main or token in vm, token
assert 'vm::configureMeshtastic' in main
assert 'enrollment_user_action' not in main.split('if(!state.advancedMode){',1)[1].split('} else {',1)[0]
assert 'data_packages_action' not in main.split('if(!state.advancedMode){',1)[1].split('} else {',1)[0]
assert 'SimpleReadinessBanner' in main
assert 'BorderStroke(1.dp,statusColor.copy(alpha=.34f))' in main
assert 'provisioning.openMeshtasticChannel(url)' in vm
print('ANDROID SIMPLE UI + MESHTASTIC SETUP CONTRACT: PASS')

assert 'advanced_section_ots' in main and 'advanced_section_radio' in main
assert 'onEnrollment' in main and 'onDataPackage' in main
