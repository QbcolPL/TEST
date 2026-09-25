from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
models = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/model/Models.kt'
simple = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/data/SimpleJson.kt'
vm = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt'
prov = ROOT / 'apps/android/app/src/main/java/org/fieldtak/hub/provision/ProvisioningController.kt'

m = models.read_text(encoding='utf-8')
s = simple.read_text(encoding='utf-8')
v = vm.read_text(encoding='utf-8')
p = prov.read_text(encoding='utf-8')

assert 'val meshtastic: MeshtasticProfile = MeshtasticProfile()' in m, 'PackageManifest must expose meshtastic profile'
assert 'meshtastic=meshProfile' in s, 'SimpleJson must map meshtastic profile into PackageManifest'
assert 'p.manifest.meshtastic' in v, 'MainViewModel must be able to consume manifest meshtastic profile'
assert 'pkg.manifest.meshtastic' in p, 'ProvisioningController must be able to consume manifest meshtastic profile'
print('ANDROID MESHTASTIC MODEL CONTRACT: PASS')
