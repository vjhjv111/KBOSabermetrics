#!/usr/bin/env python3
"""Dependency-free checks for team pages and team analytics."""
from __future__ import annotations

import json
import re
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "src" / "NaverRelay.Application" / "Teams" / "TeamPageContracts.cs"
INFRA = ROOT / "src" / "NaverRelay.Infrastructure.Sqlite"
GUI = ROOT / "src" / "NaverRelay.Gui"
API = ROOT / "src" / "NaverRelay.Api" / "Program.cs"


def require(text: str, token: str, source: str, failures: list[str]) -> None:
    if token not in text:
        failures.append(f"{source}: missing {token}")


def schema_connection() -> sqlite3.Connection:
    source = (INFRA / "DatabaseCacheService.cs").read_text(encoding="utf-8")
    match = re.search(r'private const string SchemaSql\s*=\s*"""(.*?)""";', source, re.S)
    if not match:
        raise RuntimeError("SchemaSql not found")
    connection = sqlite3.connect(":memory:")
    connection.executescript(match.group(1))
    return connection


def compile_team_sql(failures: list[str]) -> int:
    checked = 0
    connection = schema_connection()
    try:
        for path in (INFRA / "DatabaseTeamPageService.cs", INFRA / "DatabaseTeamPageService.Splits.cs"):
            text = path.read_text(encoding="utf-8")
            for match in re.finditer(r'(?:command|\w+Command)\.CommandText\s*=\s*\$?"""(.*?)""";', text, re.S):
                sql = match.group(1).strip()
                if "{" in sql or "}" in sql:
                    continue
                sql = re.sub(r'\$[A-Za-z_]\w*', 'NULL', sql)
                try:
                    connection.execute("EXPLAIN " + sql).fetchall()
                    checked += 1
                except sqlite3.Error as exc:
                    failures.append(f"{path.name}: SQL compile failed: {exc}")
    finally:
        connection.close()
    return checked


def main() -> int:
    failures: list[str] = []
    files = [
        APP,
        INFRA / "DatabaseTeamPageService.cs",
        INFRA / "DatabaseTeamPageService.Splits.cs",
        GUI / "TeamDetailForm.cs",
        GUI / "TeamSelectionDialog.cs",
    ]
    for path in files:
        if not path.exists():
            failures.append(f"missing file: {path.relative_to(ROOT)}")

    if failures:
        print(json.dumps({"pass": False, "failures": failures}, ensure_ascii=False, indent=2))
        return 1

    contracts = APP.read_text(encoding="utf-8")
    service = (INFRA / "DatabaseTeamPageService.cs").read_text(encoding="utf-8")
    splits = (INFRA / "DatabaseTeamPageService.Splits.cs").read_text(encoding="utf-8")
    detail = (GUI / "TeamDetailForm.cs").read_text(encoding="utf-8")
    main_form = (GUI / "MainForm.cs").read_text(encoding="utf-8")
    api = API.read_text(encoding="utf-8")
    database = (INFRA / "DatabaseCacheService.cs").read_text(encoding="utf-8")

    for token in (
        "interface ITeamPageService", "TeamBattingSeasonRow", "TeamPitchingSeasonRow",
        "TeamOpponentRecordRow", "TeamSituationSplitRow", "TeamPitchTypeBattingRow",
        "TeamPitchTypePitchingRow", "TeamPageData",
    ):
        require(contracts, token, APP.name, failures)

    for token in (
        "LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'", "BatterGameStats", "PitcherGameStats",
        "PlateAppearances", "Pitches", "GetTeamPageAsync", "GetTeamsAsync",
    ):
        require(service + splits, token, "DatabaseTeamPageService", failures)

    for forbidden in ("RelayParser.ParseJson", "JsonSerializer.Deserialize<NormalizedGame>", "NormalizedJson"):
        if forbidden in service or forbidden in splits:
            failures.append(f"team service contains forbidden JSON query path: {forbidden}")

    for token in (
        'AddTab("연도별 타격"', 'AddTab("연도별 투구"', 'AddTab("Value·WAR"',
        'AddTab("선수별 타격"', 'AddTab("선수별 투구"', 'AddTab("상대전적"',
        'new TabPage("상황별")', 'new TabPage("구종별")', 'AddTab("경기 로그"',
    ):
        require(detail, token, "TeamDetailForm.cs", failures)

    for token in ("_toolTeamPageButton", "InitializeTeamNavigation", "Keys.Control | Keys.T"):
        require(main_form, token, "MainForm.cs", failures)

    for token in ('MapGet("/api/teams"', 'MapGet("/api/teams/{teamCode}"', "DatabaseTeamPageService"):
        require(api, token, "NaverRelay.Api/Program.cs", failures)

    for token in (
        "IX_Games_TeamSeasonDate", "IX_PlateAppearances_BattingTeamGame",
        "IX_PlateAppearances_FieldingTeamGame",
    ):
        require(database, token, "DatabaseCacheService.cs", failures)

    sql_checked = compile_team_sql(failures)
    report = {
        "pass": not failures,
        "teamSqlStatementsCompiled": sql_checked,
        "regularSeasonOnly": "kbo_r" in service and "kbo_r" in splits,
        "apiEndpoints": ["/api/teams", "/api/teams/{teamCode}"],
        "failures": failures,
        "note": "Static/schema validation only; run dotnet build on Windows.",
    }
    output = ROOT / "validation" / "team-page-validation.json"
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
