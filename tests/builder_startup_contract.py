from pathlib import Path

root = Path(__file__).resolve().parents[1]
xaml = (root / "apps/builder/FieldTakHub.Builder/MainWindow.xaml").read_text(encoding="utf-8")
cs = (root / "apps/builder/FieldTakHub.Builder/MainWindow.xaml.cs").read_text(encoding="utf-8")
manifest = (root / "apps/builder/FieldTakHub.Builder/app.manifest").read_text(encoding="utf-8")

checks = {
    "full_hd_window_defaults": 'Height="760" Width="1280" MinHeight="600" MinWidth="980"' in xaml,
    "center_startup": 'WindowStartupLocation="CenterScreen"' in xaml,
    "per_monitor_dpi": 'PerMonitorV2' in manifest and 'true/pm' in manifest,
    "responsive_window_method": 'ConfigureResponsiveWindow()' in cs,
    "work_area_sizing": 'SystemParameters.WorkArea' in cs,
}
for name, ok in checks.items():
    print(("OK   " if ok else "FAIL ") + name)
if not all(checks.values()):
    raise SystemExit(1)
print("BUILDER FULL-HD / DPI CONTRACT: PASS")
