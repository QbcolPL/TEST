from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
XAML = ROOT / 'apps/builder/FieldTakHub.Builder/MainWindow.xaml'
s = XAML.read_text(encoding='utf-8')
assert s.count('<ScrollViewer') >= 1, 'Expected outer application ScrollViewer'
assert 'HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto" CanContentScroll="False"' in s
assert 'Width="{Binding ViewportWidth, ElementName=AppScroll}" MinWidth="1280" MinHeight="1000"' in s
# The left navigation is part of the shared application canvas, not a nested scroller.
assert '<ScrollViewer Grid.Column="0"' not in s
assert '<Border Grid.RowSpan="2" Grid.Column="0"' in s
# Advanced content is allowed to grow; it must not be clipped by fixed-height content rows.
assert '<RowDefinition Height="388"/>' not in s
assert '<RowDefinition Height="74"/>' not in s
print('BUILDER FULL-APP SCROLL CONTRACT: PASS')
