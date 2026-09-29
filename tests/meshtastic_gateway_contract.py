#!/usr/bin/env python3
from pathlib import Path
import json, xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
vm=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt').read_text()
models=(root/'apps/android/app/src/main/java/org/fieldtak/hub/model/Models.kt').read_text()
manager=(root/'apps/android/app/src/main/java/org/fieldtak/hub/meshtastic/MeshtasticManager.kt').read_text()
diag=(root/'apps/android/app/src/main/java/org/fieldtak/hub/diagnostics/ServerDiagnostics.kt').read_text()
builder=(root/'apps/builder/FieldTakHub.Builder/Services/FtakPackageBuilder.cs').read_text()
xaml=(root/'apps/builder/FieldTakHub.Builder/MainWindow.xaml').read_text()
for token in ['MeshtasticGatewayProfile','mqttPort','rootTopic','secretsExternal']:
    assert token in models, token
for token in ['p.manifest.meshtastic','meshtasticProfile()','diagnostics.run(p.manifest.server,p.manifest.meshtastic)']:
    assert token in vm, token
for token in ['gatewayConfigured','secretsExternal','mqtt_tls']:
    assert token in manager, token
for token in ['MQTT-TLS','diag_meshtastic_gateway','mqtt.await()']:
    assert token in diag, token
for token in ['gateway = new','version = 2','SecretsExternal']:
    assert token in builder, token
for token in ['AdvancedGatewayPanel','MeshtasticGatewayMqttPortBox','MeshtasticGatewayRootTopicBox','MeshtasticGatewaySecretsExternalBox']:
    assert token in xaml, token
json.loads((root/'schemas/fieldtak-v2.schema.json').read_text())
print('MESHTASTIC GATEWAY CONTRACT: PASS')
