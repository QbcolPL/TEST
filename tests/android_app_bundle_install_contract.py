from pathlib import Path
root=Path(__file__).resolve().parents[1]
vm=(root/"apps/android/app/src/main/java/org/fieldtak/hub/MainViewModel.kt").read_text()
pc=(root/"apps/android/app/src/main/java/org/fieldtak/hub/provision/ProvisioningController.kt").read_text()
b=(root/"apps/builder/FieldTakHub.Builder/Services/FtakPackageBuilder.cs").read_text()
ws=(root/"apps/builder/FieldTakHub.Builder/Services/WorkspaceService.cs").read_text()
for t in ["meshtasticApk","meshtasticApkNeedsInstall","MESHTASTIC_APP_INSTALLING","installApk(meshtasticApk)"]: assert t in vm or t in pc,t
for t in ['Path.Combine(project.SourceDirectory, "apps")','Path.Combine(payload, "apps", "meshtastic.apk")']: assert t in b,t
assert '"apps"' in ws
print("ANDROID APP BUNDLE INSTALL CONTRACT: PASS")
