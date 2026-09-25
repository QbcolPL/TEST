#!/usr/bin/env python3
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
main=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt').read_text(encoding='utf-8')
vm=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt').read_text(encoding='utf-8')
logger=root/'apps/android/app/src/main/java/org/fieldtak/hub/diagnostics/DiagnosticLogger.kt'
en=(root/'apps/android/app/src/main/res/values/strings.xml').read_text(encoding='utf-8')
pl=(root/'apps/android/app/src/main/res/values-pl/strings.xml').read_text(encoding='utf-8')
assert logger.exists()
for token in ('enum class Level','fun info','fun warning','fun error','fun recent','fun all','maxBytes','REDACTED'):
    assert token in logger.read_text(encoding='utf-8'), token
for token in ('DiagnosticLogger','APP_START','QR_PROVISIONING_RECEIVED','DIAGNOSTIC_STARTED','DIAGNOSTIC_CHECK_FAILED','MESHTASTIC_CONFIGURE_REQUESTED','OPERATION_FAILED'):
    assert token in vm, token
for token in ('LOGS','LogsScreen','diagnostic_logs','export_diagnostic_log','clear_diagnostic_log'):
    assert token in main, token
for token in ('diagnostic_logs','diagnostic_logs_explanation','diagnostic_logs_empty','export_diagnostic_log','clear_diagnostic_log','diagnostic_log_security'):
    assert f'name="{token}"' in en, token
    assert f'name="{token}"' in pl, token
assert 'Simple Mode' not in main or 'diagnostic_logs' in main
print('ANDROID DIAGNOSTIC LOGGING CONTRACT: PASS')
