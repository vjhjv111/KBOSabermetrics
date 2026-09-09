#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sqlite3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GUI = ROOT / "src" / "NaverRelay.Gui"
APP = ROOT / "src" / "NaverRelay.Application"
INFRA = ROOT / "src" / "NaverRelay.Infrastructure.Sqlite"


def main() -> int:
    failures: list[str] = []
    form = (GUI / "RecordRoomMainForm.cs").read_text(encoding="utf-8")
    program = (GUI / "Program.cs").read_text(encoding="utf-8")
    rows = (APP / "Statistics" / "RecordRoomRows.cs").read_text(encoding="utf-8")
    sql = (INFRA / "DatabaseCacheService.RecordRoom.cs").read_text(encoding="utf-8")
    schema_source = (INFRA / "DatabaseCacheService.cs").read_text(encoding="utf-8")

    for label in ("시즌기록실", "통산기록실", "팀기록실", "연도별 상수"):
        if label not in form:
            failures.append(f"top navigation missing: {label}")
    for label in ("기본", "심화", "가치", "확장", "클러치", "파워", "팀배팅", "도루", "주루", "타구", "타구방향", "투구", "구종"):
        if f'"{label}"' not in form:
            failures.append(f"batter subview missing: {label}")
    if "Application.Run(new RecordRoomMainForm())" not in program:
        failures.append("RecordRoomMainForm is not the startup form")
    if "GridFilterInfoDecorator.Apply" not in form or "Name" not in (GUI / "Services" / "GridFilterInfoDecorator.cs").read_text(encoding="utf-8"):
        failures.append("applied-filter column is not wired next to player name")
    if "RecordRoomRowFactory" not in rows:
        failures.append("record-room projection factory missing")
    for required in ("QueryBatterClutchAsync", "QueryBatterBattedBallAsync", "QueryBatterDirectionAsync", "QueryBatterPitchTypesAsync"):
        if required not in sql:
            failures.append(f"relational split query missing: {required}")
    for forbidden in ("Salary", "연봉"):
        if forbidden in rows:
            failures.append(f"unsupported field exposed in record-room rows: {forbidden}")

    match = re.search(r'private const string SchemaSql\s*=\s*"""(.*?)""";', schema_source, re.S)
    if not match:
        failures.append("SchemaSql not found")
        table_count = 0
        index_count = 0
    else:
        connection = sqlite3.connect(":memory:")
        try:
            connection.executescript(match.group(1))
            table_count = connection.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='table'").fetchone()[0]
            index_count = connection.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='index'").fetchone()[0]
        except sqlite3.Error as exc:
            failures.append(f"schema failed after record-room indexes: {exc}")
            table_count = 0
            index_count = 0
        finally:
            connection.close()

    result = {
        "pass": not failures,
        "recordRoomForm": str(GUI / "RecordRoomMainForm.cs"),
        "schemaTableCount": table_count,
        "schemaIndexCount": index_count,
        "failures": failures,
        "note": "Static UI/schema validation; run dotnet build on Windows for final compiler validation.",
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
