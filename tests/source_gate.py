#!/usr/bin/env python3
"""Bramka źródeł Field TAK Hub uruchamiana w CI przed kompilacją.

Wcześniej skrypt zawsze wypisywał PASS i niczego nie sprawdzał. Teraz blokuje powrót
problemów znalezionych w audycie (docs/AUDYT_KODU_BUILDERA.md):
  * wysyłanie treści kodów QR (w tym kluczy PSK) do zewnętrznej usługi generowania QR,
  * „atrapy” kodów QR rysowane z hasha zamiast prawdziwego kodu,
  * napisy-zaślepki „wkrótce” / „coming soon” w interfejsie,
  * nierozwiązane konflikty scalania (<<<<<<< / >>>>>>>).
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

SCAN_DIRS = [
    ROOT / "apps" / "builder",
    ROOT / "apps" / "android-hub",
]
SCAN_SUFFIXES = {".cs", ".xaml", ".kt", ".kts", ".xml"}
SKIP_PARTS = {"bin", "obj", "build", ".gradle", ".idea", ".git"}

RULES = [
    (re.compile(r"api\.qrserver\.com", re.IGNORECASE),
     "zewnętrzny generator QR – treść kodu (także PSK) wychodzi poza komputer; użyj QrService"),
    (re.compile(r"\b(DrawDeterministicQrMatrix|RenderFallbackQrBitmap)\b"),
     "atrapa kodu QR rysowana z hasha – nie da się jej zeskanować; użyj QrService"),
    (re.compile(r"wkr[oó]tce|coming soon", re.IGNORECASE),
     "napis-zaślepka w interfejsie – opisz to, co funkcja naprawdę robi"),
    (re.compile(r"^(<{7}|>{7})( |$)"),
     "nierozwiązany konflikt scalania"),
]


def iter_sources():
    for base in SCAN_DIRS:
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*")):
            if not path.is_file() or path.suffix.lower() not in SCAN_SUFFIXES:
                continue
            if SKIP_PARTS.intersection(path.relative_to(ROOT).parts):
                continue
            yield path


def main() -> int:
    # Runner Windows ma domyślnie stronę kodową cp1252 – bez tego polskie znaki w komunikatach wywracałyby skrypt.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    problems = []
    scanned = 0
    for path in iter_sources():
        scanned += 1
        text = path.read_text(encoding="utf-8", errors="replace")
        for lineno, line in enumerate(text.splitlines(), start=1):
            for pattern, reason in RULES:
                if pattern.search(line):
                    rel = path.relative_to(ROOT).as_posix()
                    problems.append(f"{rel}:{lineno}: {reason}\n    {line.strip()[:160]}")

    if problems:
        print(f"SOURCE GATE: FAIL – {len(problems)} problem(ów) w {scanned} plikach:")
        for item in problems:
            print("  " + item)
        return 1

    print(f"SOURCE GATE: PASS – sprawdzono {scanned} plików źródłowych")
    return 0


if __name__ == "__main__":
    sys.exit(main())
