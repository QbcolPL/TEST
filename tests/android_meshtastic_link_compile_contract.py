from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
main = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt'
mesh = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/meshtastic/MeshtasticManager.kt'
ms = main.read_text(encoding='utf-8')
mx = mesh.read_text(encoding='utf-8')
assert 'import org.fieldtak.hub.meshtastic.MeshtasticLinkState' in ms
assert 'ContextCompat.unregisterReceiver' not in mx
assert 'context.unregisterReceiver(receiver)' in mx
assert 'enum class MeshtasticLinkState' in mx
print('ANDROID MESHTASTIC LINK COMPILE CONTRACT: PASS')
