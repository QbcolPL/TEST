from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1] / "apps" / "android" / "app" / "src" / "main"
kt = "\n".join(p.read_text(encoding="utf-8", errors="ignore") for p in ROOT.rglob("*.kt"))
strings = set()
for p in (ROOT / "res").rglob("strings.xml"):
    strings |= set(re.findall(r'<string\s+name="([A-Za-z0-9_]+)"', p.read_text(encoding="utf-8", errors="ignore")))
refs = set(re.findall(r'R\.string\.([A-Za-z0-9_]+)', kt))
missing = sorted(refs - strings)
assert not missing, f"Missing R.string resources: {missing}"
assert "onPush(AppScreen.LOGS)" not in kt[kt.find("private fun HomeScreen"):kt.find("private fun BuyCoffeeSupportPanel")]
assert "onLogs" in kt[kt.find("private fun HomeScreen"):kt.find("private fun BuyCoffeeSupportPanel")]
vm = (ROOT / "java" / "org" / "fieldtak" / "hub" / "MainViewModel.kt").read_text(encoding="utf-8")
assert "updateMessage:String?=_state.value.updateMessage" in vm
print("ANDROID SOURCE AUDIT: PASS")
