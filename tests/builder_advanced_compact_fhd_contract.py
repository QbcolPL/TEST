from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
xaml = (ROOT / "apps/builder/FieldTakHub.Builder/MainWindow.xaml").read_text(encoding="utf-8")

assert 'x:Name="AppLayout"' in xaml
assert 'Width="{Binding ViewportWidth, ElementName=AppScroll}"' in xaml
assert 'MinWidth="1280" MinHeight="1000"' in xaml
assert 'x:Name="MainScroll"' in xaml
assert '<Grid.ColumnDefinitions>\n          <ColumnDefinition Width="1*"/>\n          <ColumnDefinition Width="1*"/>' in xaml
assert 'Grid.ColumnSpan="2"' in xaml
for name in [
    "AdvancedServerPanel", "AdvancedAtakPanel", "AdvancedFoldersPanel",
    "AdvancedMeshtasticPanel", "AdvancedGatewayPanel", "AdvancedEnrollmentPanel",
    "AdvancedSecurityPanel", "AdvancedSettingsPanel", "AdvancedModeHint"
]:
    assert f'x:Name="{name}"' in xaml, name
print("BUILDER ADVANCED COMPACT FHD CONTRACT: PASS")
