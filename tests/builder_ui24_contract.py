from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
XAML = ROOT / 'apps/builder/FieldTakHub.Builder/MainWindow.xaml'
PL = ROOT / 'apps/builder/FieldTakHub.Builder/Resources/Strings.pl.xaml'
EN = ROOT / 'apps/builder/FieldTakHub.Builder/Resources/Strings.en.xaml'

s = XAML.read_text(encoding='utf-8')
pl = PL.read_text(encoding='utf-8')
en = EN.read_text(encoding='utf-8')

required = [
    'ColumnDefinition Width="340"',
    'ColumnDefinition Width="330"',
    'RowDefinition Height="104"',
    'Text="{DynamicResource UI_Header}"',
    'Text="BETA"',
    'x:Name="LanguageBox"',
    'Style="{StaticResource DarkCombo}"',
    'Style="{StaticResource Switch}"',
    'x:Name="BuilderModeBox"',
    'x:Name="PackageExpiryBox"',
    'x:Name="QrExpiryBox"',
    'Click="SetPackage24_Click"',
    'Click="RecommendedProfile_Click"',
    'Click="PackageComponent_Click"',
    'x:Name="NavServerButton"',
    'x:Name="NavMeshtasticButton"',
    'x:Name="NavAtakButton"',
    'x:Name="NavCertsButton"',
    'x:Name="NavSettingsButton"',
    'Assets/navicons/server.png',
    'Assets/navicons/meshtastic.png',
    'Assets/navicons/atak.png',
    'Assets/navicons/certs.png',
    'Assets/navicons/settings.png',
    'x:Name="AdvancedServerPanel"',
    'x:Name="AdvancedAtakPanel"',
    'x:Name="AdvancedFoldersPanel"',
    'x:Name="AdvancedMeshtasticPanel"',
    'x:Name="AdvancedGatewayPanel"',
    'x:Name="AdvancedEnrollmentPanel"',
    'x:Name="AdvancedSecurityPanel"',
    'x:Name="AdvancedSettingsPanel"',
    'x:Name="AppScroll"',
    'Assets/field_tak_soldier.png',
    'Text="{DynamicResource UI_Status}"',
    'Text="{DynamicResource UI_Preview}"',
    'Text="{DynamicResource UI_Generated}"',
    'Text="{DynamicResource UI_Qr}"',
]
for token in required:
    assert token in s, f'missing UI 2.4 token: {token}'

for token in ['UI_Header', 'UI_NavPackageHelp', 'UI_AtakDescription', 'UI_PluginsDescription', 'UI_MapsDescription', 'UI_MeshtasticDescription', 'UI_ServerDescription', 'UI_Configured']:
    assert f'x:Key="{token}"' in pl, token
    assert f'x:Key="{token}"' in en, token

assert 'BUILDER v2.4' in pl and 'BUILDER v2.4' in en
assert 'UI 2.4 BETA' in (ROOT / 'apps/builder/README.md').read_text(encoding='utf-8')
print('BUILDER UI 2.4 CONTRACT: PASS')
