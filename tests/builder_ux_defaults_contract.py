from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
XAML = ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "MainWindow.xaml"
PL = ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "Resources" / "Strings.pl.xaml"
EN = ROOT / "apps" / "builder" / "FieldTakHub.Builder" / "Resources" / "Strings.en.xaml"

xaml = XAML.read_text(encoding="utf-8")
pl = PL.read_text(encoding="utf-8")
en = EN.read_text(encoding="utf-8")

assert 'x:Name="MeshtasticUrlBox"' in xaml and 'Text="https://meshtastic.org/' not in xaml
assert 'x:Name="EnrollmentUriBox"' in xaml and 'Text="tak://com.atakmap.app/enroll' not in xaml
assert 'UI_NavCertsHelp' in pl and 'dostępna wkrótce' in pl
assert 'UI_NavAtakHelp' in pl and 'dostępne wkrótce' in pl
assert 'UI_NavCertsHelp' in en and 'coming soon' in en
assert 'UI_NavAtakHelp' in en and 'coming soon' in en
print('BUILDER UX DEFAULTS CONTRACT: PASS')
