from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parents[1]
xaml = (root/'apps/builder/FieldTakHub.Builder/MainWindow.xaml').read_text(encoding='utf-8')
cs = (root/'apps/builder/FieldTakHub.Builder/MainWindow.xaml.cs').read_text(encoding='utf-8')
ws = (root/'apps/builder/FieldTakHub.Builder/Services/WorkspaceService.cs').read_text(encoding='utf-8')
asset = root/'apps/builder/FieldTakHub.Builder/Assets/field_tak_soldier.png'
assert 'RowDefinition Height="Auto"' in xaml
assert 'x:Name="ContentsPanel"' in xaml and 'x:Name="ExtraOptionsPanel"' in xaml
assert 'Click="ContentsHeader_Click"' in xaml and 'Click="ExtraOptionsHeader_Click"' in xaml
assert 'x:Name="ContentsChevron"' in xaml and 'x:Name="ExtraChevron"' in xaml
assert 'x:Name="ExtraPackageIdBox"' in xaml and 'x:Name="ExtraMaxDownloadsBox"' in xaml
assert 'private void ContentsHeader_Click' in cs and 'private void ExtraOptionsHeader_Click' in cs
assert 'private void ExtraFieldChanged' in cs and 'private void SyncExtraOptionsFields' in cs
assert 'EnsureDefaultProject()' in cs and 'EnsureDefaultProject()' in ws
assert 'HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" CanContentScroll="False"' in xaml
im=Image.open(asset)
assert im.size == (322,351), im.size
print('BUILDER UI 2.4 FIX6 CONTRACT: PASS')
