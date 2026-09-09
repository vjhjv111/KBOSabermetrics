#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
INFRA = SRC / "NaverRelay.Infrastructure.Sqlite"


def schema_sql() -> str:
    text = (INFRA / "DatabaseCacheService.cs").read_text(encoding="utf-8")
    match = re.search(r'private const string SchemaSql\s*=\s*"""(.*?)""";', text, re.S)
    if not match:
        raise RuntimeError("SchemaSql not found")
    return match.group(1)


def explain(connection: sqlite3.Connection, name: str, sql: str, failures: list[str]) -> None:
    try:
        connection.execute("EXPLAIN " + sql).fetchall()
    except sqlite3.Error as exc:
        failures.append(f"{name}: {exc}")


def main() -> int:
    failures: list[str] = []
    form = (SRC / "NaverRelay.Gui/RecordRoomMainForm.cs").read_text(encoding="utf-8")
    api = (SRC / "NaverRelay.Api/Program.cs").read_text(encoding="utf-8")
    contracts = (SRC / "NaverRelay.Application/Queries/RecordRoomContracts.cs").read_text(encoding="utf-8")
    rows = (SRC / "NaverRelay.Application/Statistics/PitcherRecordRoomRows.cs").read_text(encoding="utf-8")
    core = (INFRA / "DatabaseCacheService.PitcherRecordRoom.Core.cs").read_text(encoding="utf-8")
    batted = (INFRA / "DatabaseCacheService.PitcherRecordRoom.Batted.cs").read_text(encoding="utf-8")

    expected_tabs = ["기본", "심화", "가치", "확장", "WP", "주자", "선발", "구원", "타구", "타구방향", "투구", "구종"]
    pitcher_tab_literal = 'new[] { ' + ', '.join(f'"{item}"' for item in expected_tabs) + ' }'
    if pitcher_tab_literal not in form:
        failures.append("pitcher tab order does not match screenshot-derived order")

    for method in [
        "GetExtendedAsync", "GetWinProbabilityAsync", "GetRunnerAsync", "GetStarterAsync",
        "GetRelieverAsync", "GetBattedBallAsync", "GetDirectionAsync", "GetPitchProfileAsync", "GetPitchTypesAsync",
    ]:
        if method not in contracts:
            failures.append(f"contract missing {method}")
        if method not in form:
            failures.append(f"GUI missing {method}")

    for method in [
        "QueryPitcherExtendedAsync", "QueryPitcherWinProbabilityAsync", "QueryPitcherRunnerAsync",
        "QueryPitcherStarterAsync", "QueryPitcherRelieverAsync", "QueryPitcherBattedBallAsync",
        "QueryPitcherDirectionAsync", "QueryPitcherPitchProfileAsync", "QueryPitcherPitchTypesAsync",
    ]:
        if method not in core + batted:
            failures.append(f"SQLite implementation missing {method}")

    for class_name in [
        "PitcherBasicRecordRow", "PitcherAdvancedRecordRow", "PitcherDetailedValueRecordRow",
        "PitcherExtendedRecordRow", "PitcherWinProbabilityRecordRow", "PitcherRunnerRecordRow",
        "PitcherStarterRecordRow", "PitcherRelieverRecordRow", "PitcherBattedBallRecordRow",
        "PitcherDirectionRecordRow", "PitcherPitchProfileRecordRow", "PitcherPitchTypeRecordRow",
    ]:
        if f"class {class_name}" not in rows:
            failures.append(f"row type missing {class_name}")

    if '/api/record-room/pitchers/{view}' not in api:
        failures.append("pitcher record-room API route missing")
    if "IPitcherRecordRoomQueryService" not in api:
        failures.append("pitcher record-room service is not registered in API")

    if any(token in rows for token in ["연봉", "Salary"]):
        failures.append("unsupported salary fields must not be exposed")

    conn = sqlite3.connect(":memory:")
    try:
        conn.executescript(schema_sql())
        cte = "WITH FilteredGames AS (SELECT * FROM Games WHERE 1=1)"
        identity = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode), MAX(COALESCE(NULLIF(pa.FinalPitcherName,''),pa.PitcherName)), pa.FieldingTeamCode"
        group = "COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode), pa.FieldingTeamCode"
        common_where = "pa.IsOfficial=1 AND COALESCE(NULLIF(pa.FinalPitcherPcode,''),pa.PitcherPcode)<>''"

        explain(conn, "batted-ball", f"""
            {cte}
            SELECT {identity}, COUNT(*), SUM(pa.IsHit), SUM(pa.TotalBases)
            FROM PlateAppearances pa JOIN FilteredGames g ON g.GameId=pa.GameId
            WHERE {common_where}
            GROUP BY {group}
        """, failures)

        explain(conn, "direction", f"""
            {cte}, RankedStance AS (
                SELECT p.PlateAppearanceId, p.BatterStance,
                       ROW_NUMBER() OVER(PARTITION BY p.PlateAppearanceId ORDER BY p.ActualPitchIndex DESC) rn
                FROM Pitches p JOIN FilteredGames fg ON fg.GameId=p.GameId
            )
            SELECT {identity}, SUM(CASE WHEN pa.FieldDirection BETWEEN 1 AND 5 THEN 1 ELSE 0 END)
            FROM PlateAppearances pa JOIN FilteredGames g ON g.GameId=pa.GameId
            LEFT JOIN RankedStance rs ON rs.PlateAppearanceId=pa.PlateAppearanceId AND rs.rn=1
            WHERE {common_where}
            GROUP BY {group}
        """, failures)

        explain(conn, "pitch-profile", f"""
            {cte}, RankedPitches AS (
                SELECT p.*, ROW_NUMBER() OVER(PARTITION BY p.PlateAppearanceId ORDER BY p.ActualPitchIndex DESC) reverse_rank
                FROM Pitches p JOIN FilteredGames fg ON fg.GameId=p.GameId
            )
            SELECT p.PitcherPcode, MAX(p.PitcherName), pa.FieldingTeamCode,
                   COUNT(*), SUM(p.IsCalledStrike), SUM(p.IsWhiff), SUM(p.IsInNominalStrikeZone)
            FROM RankedPitches p JOIN FilteredGames g ON g.GameId=p.GameId
            LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId
            WHERE COALESCE(p.PitcherPcode,'')<>''
            GROUP BY p.PitcherPcode, pa.FieldingTeamCode
        """, failures)

        explain(conn, "pitch-types", f"""
            {cte}, RankedPitches AS (
                SELECT p.*, ROW_NUMBER() OVER(PARTITION BY p.PlateAppearanceId ORDER BY p.ActualPitchIndex DESC) reverse_rank
                FROM Pitches p JOIN FilteredGames fg ON fg.GameId=p.GameId
            )
            SELECT rp.PitcherPcode, MAX(rp.PitcherName), pa.FieldingTeamCode, COALESCE(rp.PitchType,''),
                   COUNT(*), SUM(rp.SpeedKmh), SUM(CASE WHEN rp.reverse_rank=1 THEN pa.TotalBases ELSE 0 END)
            FROM RankedPitches rp JOIN FilteredGames g ON g.GameId=rp.GameId
            LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=rp.PlateAppearanceId
            WHERE COALESCE(rp.PitcherPcode,'')<>''
            GROUP BY rp.PitcherPcode, pa.FieldingTeamCode, COALESCE(rp.PitchType,'')
        """, failures)
    finally:
        conn.close()

    report = {
        "pass": not failures,
        "pitcherTabs": expected_tabs,
        "schemaIndexCount": len(re.findall(r"CREATE INDEX IF NOT EXISTS", schema_sql(), re.I)),
        "apiRoute": "/api/record-room/pitchers/{view}",
        "failures": failures,
        "note": "Static/schema validation only; run dotnet build on Windows for final compiler validation.",
    }
    output = ROOT / "validation/pitcher-record-room-validation.json"
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
