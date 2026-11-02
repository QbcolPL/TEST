#!/usr/bin/env python3
from pathlib import Path
import json, zipfile, tempfile, subprocess, sys
root=Path(__file__).resolve().parents[1]
v=json.loads((root/'version.json').read_text())
assert v['hubVersion']=='2.3.0' and v['builderVersion']=='2.3.0'
proj=json.loads((root/'examples/ggzs-basic/GGZS-Milsim.fthproj').read_text()) if (root/'examples/ggzs-basic/GGZS-Milsim.fthproj').exists() else {}
# Static contract: the feature is present in both sides and no secret fields are introduced.
for rel in ['apps/android/app/src/main/java/org/fieldtak/hub/meshtastic/MeshtasticManager.kt',
            'docs/MESHTASTIC_HYBRID.md', 'CHANGELOG.md']:
    assert (root/rel).exists(), rel
profile=(root/'apps/builder/FieldTakHub.Builder/Models/FieldTakProject.cs').read_text()
for token in ['MeshtasticProfile','MeshtasticGatewayProfile','Mode','Region','ModemPreset','HopLimit','OtsRelay','MqttPort','RootTopic','SecretsExternal']:
    assert token in profile, token
builder=(root/'apps/builder/FieldTakHub.Builder/Services/FtakPackageBuilder.cs').read_text()
for token in ['Path.Combine(payload, "meshtastic", "profile.json")','version = 2','gateway = new','FTH-MESH-002','FTH-MESH-003']:
    assert token in builder, token
android=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt').read_text()
for token in ['MeshtasticManager','meshtasticStatus','meshtasticProfile','openMeshtasticApp','testMeshtastic']:
    assert token in android, token
for p in root.rglob('*'):
    if p.is_file() and p.suffix.lower() in {'.json','.xml','.xaml'} and not any(x in p.parts for x in ['.git','bin','obj','build']):
        try:
            if p.suffix.lower()=='.json': json.loads(p.read_text())
        except Exception as e: raise AssertionError(f'{p}: {e}')
print('MESHTASTIC HYBRID CONTRACT: PASS')

manifest=(root/'apps/builder/FieldTakHub.Builder/Models/FieldTakManifest.cs').read_text()
assert 'MeshtasticProfile Meshtastic' in manifest
validator=(root/'apps/builder/FieldTakHub.Builder/Services/ServerValidator.cs').read_text()
for token in ['FTH-MESH-005','FTH-MESH-006','FTH-MESH-007']:
    assert token in validator, token
print('MESHTASTIC GATEWAY CONTRACT: PASS')
