#!/usr/bin/env python3
from pathlib import Path
import json,re,sys,xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
errors=[]
def fail(msg): errors.append(msg)
def read(p): return (ROOT/p).read_text(encoding='utf-8')

def xml_keys(path):
    tree=ET.parse(ROOT/path); return {e.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key') for e in tree.getroot() if e.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key')}

def android_keys(path):
    tree=ET.parse(ROOT/path); return {e.attrib['name'] for e in tree.getroot().findall('string')}

v=json.loads(read('version.json'))
if v.get('releaseChannel') not in {'rc','stable'}: fail('version.json releaseChannel must be rc or stable')
gradle=read('apps/android/app/build.gradle.kts')
csproj=read('apps/builder/FieldTakHub.Builder/FieldTakHub.Builder.csproj')
if f'versionCode = {v["androidVersionCode"]}' not in gradle: fail('Android versionCode != version.json')
if f'versionName = "{v["hubVersion"]}"' not in gradle: fail('Android versionName != version.json')
if f'<Version>{v["builderVersion"]}</Version>' not in csproj: fail('Builder Version != version.json')
# releaseChannel has a single canonical value in version.json; implementation may embed it for runtime, but gate does not demand it in csproj.
if f'RELEASE_CHANNEL", "\\"{v["releaseChannel"]}\\""' not in gradle: fail('Android runtime release channel does not match version.json')

en=android_keys('apps/android/app/src/main/res/values/strings.xml'); pl=android_keys('apps/android/app/src/main/res/values-pl/strings.xml')
if en!=pl: fail(f'Android PL/EN resource mismatch: only-en={sorted(en-pl)}, only-pl={sorted(pl-en)}')
wen=xml_keys('apps/builder/FieldTakHub.Builder/Resources/Strings.en.xaml'); wpl=xml_keys('apps/builder/FieldTakHub.Builder/Resources/Strings.pl.xaml')
if wen!=wpl: fail(f'Builder PL/EN resource mismatch: only-en={sorted(wen-wpl)}, only-pl={sorted(wpl-wen)}')


# Every Android R.string reference in production Kotlin must exist in the base English resources.
android_refs=set()
for kp in (ROOT/'apps/android/app/src/main/java').rglob('*.kt'):
    android_refs.update(re.findall(r'R\.string\.([A-Za-z0-9_]+)', kp.read_text(encoding='utf-8')))
missing_android=android_refs-en
if missing_android: fail(f'Android missing string resources referenced from Kotlin: {sorted(missing_android)}')

# Every WPF DynamicResource reference must exist in the English resource dictionary.
wpf_refs=set()
for xp in (ROOT/'apps/builder/FieldTakHub.Builder').rglob('*.xaml'):
    if 'Resources' in xp.parts: continue
    wpf_refs.update(re.findall(r'\{DynamicResource\s+([A-Za-z0-9_.-]+)\}', xp.read_text(encoding='utf-8')))
missing_wpf=wpf_refs-wen
if missing_wpf: fail(f'Builder missing DynamicResource keys referenced from XAML: {sorted(missing_wpf)}')

# Diagnostic logging contract
log_contract=ROOT/'tests/android_diagnostic_logging_contract.py'
if not log_contract.exists(): fail('Android diagnostic logging contract missing')

# Package integrity contract: Builder signs META-INF/server.txt and Android must compare all signed files,
# excluding only the cryptographic metadata that cannot sign itself.
repo=read('apps/android/app/src/main/java/org/fieldtak/hub/data/PackageRepository.kt')
builder=read('apps/builder/FieldTakHub.Builder/Services/FtakPackageBuilder.cs')
if 'META-INF/server.txt' not in builder and 'Path.Combine(meta, "server.txt")' not in builder:
    fail('Builder no longer emits signed META-INF/server.txt')
for required_unsigned in ('META-INF/checksums.sha256','META-INF/signature.ed25519','META-INF/publisher.pub'):
    if required_unsigned not in repo: fail(f'Android verifier unsigned-meta contract missing {required_unsigned}')
if 'actualSignedFiles != listed.toSet()' not in repo:
    fail('Android verifier does not enforce exact signed-file list equality')

# Provision descriptor must carry pre-download storage hints end-to-end.
dist=read('apps/builder/FieldTakHub.Builder/Services/DistributionServer.cs')
models=read('apps/android/app/src/main/java/org/fieldtak/hub/model/Models.kt')
vm=read('apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt')
for token in ('packageBytes','recommendedFreeBytes'):
    if token not in dist: fail(f'Builder provision descriptor missing {token}')
    if token not in models: fail(f'Android provision descriptor model missing {token}')
    if token not in vm: fail(f'Android pre-download storage check missing {token}')

# Known compile-contract checks learned from real RC CI runs.
main_activity=read('apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt')
if 'setContent{' in main_activity and 'import androidx.activity.compose.setContent' not in main_activity:
    fail('Android MainActivity uses setContent without androidx.activity.compose.setContent import')
if re.search(r'onClick\s*=\s*\{\s*back\s*\}', main_activity):
    fail('Android onClick contains bare local back function instead of invoking/passing it')
if re.search(r'progress\s*=\s*0\s+to\s+null', vm):
    fail('Android progress pair uses Int zero; expected Long')
# WPF/.NET implicit-usings differ from plain SDK projects. Any Builder source file that
# uses System.IO types must import System.IO explicitly so CI and local WPF builds agree.
io_symbols = re.compile(r'\b(?:Path|File|Directory|FileInfo|DirectoryInfo|InvalidDataException|DirectoryNotFoundException|FileNotFoundException|StreamWriter|MemoryStream|SearchOption|FileStream|Stream)\b')
for cp in (ROOT/'apps/builder/FieldTakHub.Builder').rglob('*.cs'):
    txt=cp.read_text(encoding='utf-8')
    if io_symbols.search(txt) and 'using System.IO;' not in txt and 'global using System.IO;' not in txt:
        fail(f'Builder compile contract missing explicit System.IO in {cp.relative_to(ROOT)}')
if 'Uri.EscapeUriString(' in dist:
    fail('Builder uses obsolete Uri.EscapeUriString')
workspace=read('apps/builder/FieldTakHub.Builder/Services/WorkspaceService.cs')
for token in ('SourceFolders', 'EnsureDefaultProject', 'EnsureSourceTree', 'CreateNewProject', 'README-WRZUC-PLIKI-TUTAJ.txt'):
    if token not in workspace: fail(f'Builder workspace automation missing {token}')
for token in ('meshtastic',):
    if token not in workspace or token not in builder: fail(f'Builder Meshtastic package workspace contract missing {token}')
main_xaml=read('apps/builder/FieldTakHub.Builder/MainWindow.xaml')
for token in ('NewProject_Click', 'ProjectFolder_Drop', 'RepairFolders_Click'):
    if token not in main_xaml: fail(f'Builder project-folder UI missing {token}')
for token in ('ImportLegacy_Click', 'ImportLegacy'):
    if token not in main_xaml: fail(f'Builder legacy-import UI missing {token}')
for token in ('CloudDistribution','GenerateCloudQr_Click','TestCloudLink_Click','CloudPackageBox','CloudUrlBox'):
    if token not in main_xaml: fail(f'Builder cloud-distribution UI missing {token}')
cloud=ROOT/'apps/builder/FieldTakHub.Builder/Services/CloudDistributionService.cs'
if not cloud.exists(): fail('Builder CloudDistributionService missing')
else:
    cloud_text=cloud.read_text(encoding='utf-8')
    for token in ('RequireHttps','ResolvePackageUrl','drive.usercontent.google.com','FTH-CLOUD-003','CreateDeepLink','SHA256.HashData','packageUrl=','packageBytes=','expiresUtc='):
        if token not in cloud_text: fail(f'Builder cloud distribution contract missing {token}')
legacy=ROOT/'apps/builder/FieldTakHub.Builder/Services/LegacyPackageImporter.cs'
if not legacy.exists(): fail('Builder legacy 1.x importer missing')
else:
    legacy_text=legacy.read_text(encoding='utf-8')
    for token in ('RSASignaturePadding.Pkcs1','SHA256.HashData','manifest.sig','manifest.pub.pem','LEGACY-IMPORT-REPORT.txt'):
        if token not in legacy_text: fail(f'Builder legacy importer contract missing {token}')
for token in ('payload, "atak", "atak.apk"','FTH-BLD-006'):
    if token not in builder: fail(f'Builder direct ATAK APK contract missing {token}')
mission=read('apps/builder/FieldTakHub.Builder/Services/MissionPackageBuilder.cs')
if '!Path.GetExtension(f).Equals(".apk", StringComparison.OrdinalIgnoreCase)' not in mission:
    fail('Mission Package builder does not exclude ATAK APK files')
builder_tests=read('apps/builder/FieldTakHub.Builder.Tests/BuilderCoreTests.cs')
for required_using in ('using System;','using System.IO;','using System.Linq;'):
    if required_using not in builder_tests:
        fail(f'Builder tests compile contract missing {required_using}')

# Visual identity contract: retain the proven 1.9 launcher assets and brand palette.
brand_theme=read('apps/android/app/src/main/java/org/fieldtak/hub/ui/FieldTakTheme.kt')
for token in ('0xFF0B1114','0xFF102127','0xFF28E0D7','0xFFFF3B4E','0xFF59D17D'):
    if token not in brand_theme: fail(f'Android 1.9 brand palette missing {token}')
manifest_text=read('apps/android/app/src/main/AndroidManifest.xml')
for token in ('android:icon="@mipmap/ic_launcher"','android:roundIcon="@mipmap/ic_launcher_round"'):
    if token not in manifest_text: fail(f'Android launcher icon contract missing {token}')

# Universal QR routing: cloud .ftak, OpenTAK/ATAK enrollment and data-package import.
provisioning=read('apps/android/app/src/main/java/org/fieldtak/hub/provision/ProvisioningController.kt')
for token in ('handleInput','packageUrl','openTakUri','openTakImportUrl'):
    haystack=vm if token in ('handleInput','packageUrl') else provisioning
    if token not in haystack: fail(f'Android universal QR contract missing {token}')
for token in ('handleProvisioningQr','handleEnrollmentQr','handleDataPackageQr','handOffMeshtasticChannel','isMeshtasticInstalled','meshtasticChannelUrl','openMeshtasticChannel'):
    if token not in vm: fail(f'Android QR/Meshtastic workflow missing {token}')
for token in ('DEVICE_STATUS','SCAN_UNIVERSAL','DeviceStatusScreen','device_status_action','scan_any_qr','status_meshtastic','meshtasticChannel'):
    if token not in main_activity: fail(f'Android device-status UI missing {token}')
for token in ('meshtastic.org/e/','vm_meshtastic_not_installed'):
    if token not in vm: fail(f'Android Meshtastic routing contract missing {token}')
for token in ('payload/meshtastic/channel.url','https://meshtastic.org/e/'):
    if token not in provisioning and token not in vm: fail(f'Android bundled Meshtastic channel contract missing {token}')
for token in ('enrollment', 'enroll.url'):
    if token not in workspace and token not in builder: fail(f'Builder one-scan enrollment package workspace contract missing {token}')
project_model=read('apps/builder/FieldTakHub.Builder/Models/FieldTakProject.cs')
manifest_model=read('apps/builder/FieldTakHub.Builder/Models/FieldTakManifest.cs')
if 'EnrollmentProfile' not in project_model or 'EnrollmentProfile' not in manifest_model: fail('Builder enrollment profile model missing')
if 'EnrollmentConfigTitle' not in main_xaml or 'EnrollmentUriBox' not in main_xaml: fail('Builder enrollment UI missing')
for token in ('openEnrollmentUri','enrollmentUri'):
    if token not in provisioning and token not in vm: fail(f'Android one-scan enrollment workflow missing {token}')
if 'OTS_ENROLLMENT' not in read('apps/android/app/src/main/java/org/fieldtak/hub/deployment/DeploymentModels.kt'): fail('Android enrollment deployment stage missing')
for token in ('ACTION_MANAGE_UNKNOWN_APP_SOURCES','canRequestPackageInstalls'):
    if token not in provisioning: fail(f'Android unknown-sources handoff missing {token}')
for asset in ('apps/android/app/src/main/res/drawable/ic_launcher_foreground.xml','apps/builder/FieldTakHub.Builder/Assets/tak-field-hub-icon-1.9.png','apps/builder/FieldTakHub.Builder/Assets/tak-field-hub-builder.ico'):
    if not (ROOT/asset).exists(): fail(f'Missing 1.9 visual asset: {asset}')

# Parse source JSON/XML/XAML.
for p in ROOT.rglob('*.json'):
    if any(x in p.parts for x in ('.git','build','bin','obj')): continue
    try: json.loads(p.read_text(encoding='utf-8'))
    except Exception as e: fail(f'JSON parse {p.relative_to(ROOT)}: {e}')
for ext in ('*.xml','*.xaml'):
    for p in ROOT.rglob(ext):
        if any(x in p.parts for x in ('.git','build','bin','obj')): continue
        try: ET.parse(p)
        except Exception as e: fail(f'XML/XAML parse {p.relative_to(ROOT)}: {e}')

# Large packages/maps must be hashed as streams, never loaded wholly into RAM.
for cp in (ROOT/'apps/builder').rglob('*.cs'):
    txt=cp.read_text(encoding='utf-8')
    if re.search(r'SHA256\.HashData\(\s*(?:await\s+)?File\.ReadAllBytes',txt):
        fail(f'Non-streaming SHA-256 in Builder: {cp.relative_to(ROOT)}')

# No production secrets/certificate material belongs in source tree.
for p in ROOT.rglob('*'):
    if not p.is_file() or '.git' in p.parts: continue
    if p.suffix.lower() in {'.p12','.pfx','.jks','.keystore','.key'} and not p.name.endswith('.example'):
        fail(f'Potential secret/certificate file committed: {p.relative_to(ROOT)}')

required=[
 'apps/android/app/src/main/java/org/fieldtak/hub/update/UpdateService.kt',
 'apps/android/app/src/main/java/org/fieldtak/hub/security/UrlPolicy.kt',
 'apps/android/app/src/main/java/org/fieldtak/hub/storage/StorageMaintenance.kt',
 'apps/builder/FieldTakHub.Builder/Services/UpdateService.cs',
 'apps/builder/FieldTakHub.Builder/Services/ServerDiagnosticsService.cs',
 'apps/builder/FieldTakHub.Builder/Services/ServerValidator.cs',
 'docs/ADMIN_GUIDE.md','docs/BUILD_AND_SIGN.md','docs/UPDATES.md','docs/ACCESSIBILITY.md','docs/RELEASE_CHECKLIST.md'
]
for r in required:
    if not (ROOT/r).exists(): fail(f'Missing required release file: {r}')

release=read('.github/workflows/release.yml') if (ROOT/'.github/workflows/release.yml').exists() else ''
if 'assembleRelease' not in release: fail('Release workflow must build assembleRelease')
if 'fieldtak-release.json' not in release: fail('Release workflow must publish fieldtak-release.json')
if 'assembleDebug' in release: fail('Release workflow must not publish a debug APK')

if errors:
    print('SOURCE GATE: FAIL')
    for e in errors: print(' -',e)
    sys.exit(1)
print('SOURCE GATE: PASS')
print(f'Hub={v["hubVersion"]} Builder={v["builderVersion"]} channel={v["releaseChannel"]}')
print(f'Android strings={len(en)} Builder strings={len(wen)}')
