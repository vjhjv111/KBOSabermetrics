#!/usr/bin/env python3
"""Run every dependency-free V2 validation script."""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = [
    "validate_v2_architecture.py",
    "static_source_check.py",
    "validate_relational_warehouse.py",
    "validate_player_profile_data.py",
    "validate_team_pages.py",
    "validate_record_room_ui.py",
    "validate_pitcher_record_room.py",
    "validate_kbo_pitcher_war_v3.py",
    "validate_pitcher_war_diagnostics.py",
]


def main() -> int:
    failures: list[str] = []
    for name in SCRIPTS:
        path = ROOT / "validation" / name
        print(f"\n=== {name} ===", flush=True)
        completed = subprocess.run([sys.executable, str(path)], cwd=ROOT, check=False)
        if completed.returncode != 0:
            failures.append(name)
    print()
    if failures:
        print("V2 static validation: FAIL")
        for name in failures:
            print(f"  - {name}")
        return 1
    print("V2 static validation: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
