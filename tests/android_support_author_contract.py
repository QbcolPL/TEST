#!/usr/bin/env python3
from pathlib import Path
root=Path(__file__).resolve().parents[1]
main=(root/'apps/android/app/src/main/java/org/fieldtak/hub/MainActivity.kt').read_text(encoding='utf-8')
strings=(root/'apps/android/app/src/main/res/values/strings.xml').read_text(encoding='utf-8')
strings_pl=(root/'apps/android/app/src/main/res/values-pl/strings.xml').read_text(encoding='utf-8')
qr=root/'apps/android/app/src/main/res/drawable-nodpi/buycoffee_qr_qbcol.png'
assert 'BuyCoffeeSupportPanel' in main
assert 'https://buycoffee.to/qbcol' in main
assert 'buycoffee_qr_qbcol' in main
assert qr.exists() and qr.stat().st_size > 1000
for token in ['support_author_title','support_author_detail','support_author_action','support_author_qr']:
    assert f'name="{token}"' in strings, token
    assert f'name="{token}"' in strings_pl, token
assert 'BuyCoffeeSupportPanel()' in main
print('ANDROID SUPPORT AUTHOR CONTRACT: PASS')
