from pathlib import Path
import subprocess, sys
root=Path(__file__).resolve().parents[1]
checks=[
    [sys.executable, str(root/'tests/source_gate.py')],
    [sys.executable, str(root/'tests/meshtastic_hybrid_contract.py')],
    [sys.executable, str(root/'tests/meshtastic_gateway_contract.py')],
    [sys.executable, str(root/'tests/android_actions_contract.py')],
    [sys.executable, str(root/'tests/builder_startup_contract.py')],
    [sys.executable, str(root/'tests/builder_ui24_contract.py')],
    [sys.executable, str(root/'tests/builder_full_app_scroll_contract.py')],
    [sys.executable, str(root/'tests/builder_ui24_fix6_contract.py')],
]
for cmd in checks:
    r=subprocess.run(cmd,cwd=root)
    if r.returncode: raise SystemExit(r.returncode)
print('FULL STATIC GATE: PASS')
