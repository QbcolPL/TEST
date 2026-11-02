from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
X=ROOT/'apps/builder/FieldTakHub.Builder/MainWindow.xaml'
CS=ROOT/'apps/builder/FieldTakHub.Builder/MainWindow.xaml.cs'
IMP=ROOT/'apps/builder/FieldTakHub.Builder/Services/FtakPackageImporter.cs'
xs=X.read_text(encoding='utf-8'); cs=CS.read_text(encoding='utf-8')
assert IMP.exists()
assert 'ImportFtak_Click' in cs and 'FtakPackageImporter' in cs
assert 'x:Name="QrImage"' in xs and 'Click="GenerateCloudQr_Click"' in xs
assert 'Google Drive' in (ROOT/'apps/builder/FieldTakHub.Builder/Services/CloudDistributionService.cs').read_text(encoding='utf-8')
assert 'ContentMapsSizeText' in xs and 'ContentPluginsSizeText' in xs
assert '156 MB' not in xs and '92 MB' not in xs and '28 MB' not in xs
print('BUILDER FTAK IMPORT/DISTRIBUTION CONTRACT: PASS')
