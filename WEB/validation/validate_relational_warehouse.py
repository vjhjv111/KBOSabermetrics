#!/usr/bin/env python3
"""Validate the relational SQLite warehouse without requiring the .NET SDK.

This does not replace a C# compiler. It validates the executable SQLite schema,
INSERT column shapes, SQL syntax against that schema, DB-only query boundaries,
and the bundled raw sample's required source fields.
"""
from __future__ import annotations

import json
import re
import sqlite3
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "NaverRelay.Infrastructure.Sqlite"


def extract_schema() -> str:
    text = (SRC / "DatabaseCacheService.cs").read_text(encoding="utf-8")
    match = re.search(r'private const string SchemaSql\s*=\s*"""(.*?)""";', text, re.S)
    if not match:
        raise RuntimeError("SchemaSql raw string was not found")
    return match.group(1)


def table_columns(connection: sqlite3.Connection) -> dict[str, list[str]]:
    names = [row[0] for row in connection.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"
    )]
    return {
        name: [row[1] for row in connection.execute(f'PRAGMA table_info("{name}")')]
        for name in names
    }


def split_csv(text: str) -> list[str]:
    parts: list[str] = []
    depth = 0
    start = 0
    quote: str | None = None
    i = 0
    while i < len(text):
        char = text[i]
        if quote:
            if char == quote:
                if i + 1 < len(text) and text[i + 1] == quote:
                    i += 2
                    continue
                quote = None
        else:
            if char in "'\"":
                quote = char
            elif char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
            elif char == "," and depth == 0:
                parts.append(text[start:i].strip())
                start = i + 1
        i += 1
    tail = text[start:].strip()
    if tail:
        parts.append(tail)
    return parts


def validate_insert_shapes(columns: dict[str, list[str]]) -> tuple[int, list[str]]:
    failures: list[str] = []
    checked = 0
    for path in sorted(SRC.glob("DatabaseCacheService*.cs")):
        text = path.read_text(encoding="utf-8")
        raw_strings = re.findall(r'"""(.*?)"""', text, re.S)
        for sql in raw_strings:
            for match in re.finditer(
                r'INSERT(?:\s+OR\s+REPLACE)?\s+INTO\s+(\w+)\s*'
                r'(?:\((.*?)\))?\s*VALUES\s*\((.*?)\)',
                sql,
                re.I | re.S,
            ):
                table, column_text, value_text = match.groups()
                if table not in columns:
                    failures.append(f"{path.name}: INSERT references missing table {table}")
                    continue
                insert_columns = split_csv(column_text) if column_text else columns[table]
                values = split_csv(value_text)
                checked += 1
                if len(insert_columns) != len(values):
                    failures.append(
                        f"{path.name}: {table} INSERT has {len(insert_columns)} columns and {len(values)} values"
                    )
                unknown = [name.strip().strip('"[]`') for name in insert_columns
                           if name.strip().strip('"[]`') not in columns[table]]
                if unknown:
                    failures.append(f"{path.name}: {table} INSERT has unknown columns {unknown}")
    return checked, failures


def prepare_sql_for_explain(sql: str) -> str | None:
    sql = sql.strip()
    if not sql:
        return None
    sql = sql.replace(
        "{filter.Cte}",
        "WITH FilteredGames AS (SELECT * FROM Games WHERE 1=1)",
    )
    # Other C# interpolations are runtime expressions that this checker cannot safely substitute.
    if "{" in sql or "}" in sql:
        return None
    sql = re.sub(r'\$[A-Za-z_][A-Za-z0-9_]*', 'NULL', sql)
    return sql


def split_sql_statements(script: str) -> list[str]:
    statements: list[str] = []
    buffer = ""
    for line in script.splitlines(True):
        buffer += line
        if sqlite3.complete_statement(buffer):
            statement = buffer.strip().rstrip(";").strip()
            if statement:
                statements.append(statement)
            buffer = ""
    if buffer.strip():
        statements.append(buffer.strip())
    return statements


def validate_sql(connection: sqlite3.Connection) -> tuple[int, int, list[str]]:
    checked = 0
    skipped = 0
    failures: list[str] = []
    for path in sorted(SRC.glob("Database*.cs")):
        text = path.read_text(encoding="utf-8")
        for match in re.finditer(r'(?:command|\w+Command)\.CommandText\s*=\s*\$?"""(.*?)""";', text, re.S):
            prepared = prepare_sql_for_explain(match.group(1))
            if prepared is None:
                skipped += 1
                continue
            for statement in split_sql_statements(prepared):
                upper = statement.lstrip().upper()
                # Schema DDL is executed separately. Runtime PRAGMAs/VACUUM are valid SQLite commands
                # but do not accept EXPLAIN consistently across Python SQLite builds.
                if upper.startswith(("CREATE ", "PRAGMA ", "VACUUM", "ANALYZE")):
                    continue
                try:
                    connection.execute("EXPLAIN " + statement).fetchall()
                    checked += 1
                except sqlite3.Error as exc:
                    failures.append(f"{path.name}: SQL compile failed: {exc}: {statement[:180]!r}")
    return checked, skipped, failures


def validate_architecture() -> list[str]:
    failures: list[str] = []
    service = (SRC / "DatabaseCacheService.cs").read_text(encoding="utf-8")
    parsing = (ROOT / "src/NaverRelay.Gui/Services/ParsingWorkflowService.cs").read_text(encoding="utf-8")
    active = [
        ROOT / "src/NaverRelay.Gui/MainForm.cs",
        SRC / "DatabaseAnalyticsService.cs",
        SRC / "DatabasePlayerPageService.cs",
        SRC / "DatabaseCacheService.Query.cs",
        SRC / "DatabaseCacheService.Raw.cs",
        SRC / "DatabaseCacheService.Analytics.cs",
    ]
    if 'sabermetrics_v2.db' not in service:
        failures.append("The warehouse DB does not use its own new database file")
    if 'NormalizedJson TEXT' in service or 'SourceJson TEXT' in service:
        failures.append("A source/normalized game JSON column remains in the relational schema")
    if 'RelayParser.ParseJson(json)' not in parsing or 'SaveGameAndSourceAsync(game, document' not in parsing:
        failures.append("The one-time JSON import pipeline is incomplete")
    for path in active:
        text = path.read_text(encoding="utf-8")
        for forbidden in ('RelayParser.ParseJson(', 'JsonSerializer.Deserialize<NormalizedGame>', 'LoadAllGamesAsync'):
            if forbidden in text:
                failures.append(f"{path.name}: active query path contains {forbidden}")
    if (SRC / "PlayerPageService.cs").exists():
        failures.append("The obsolete all-games in-memory PlayerPageService still exists")
    return failures


def validate_sample() -> dict[str, int | list[str]]:
    sample = ROOT / "SampleData" / "2026.zip"
    issues: list[str] = []
    game_count = 0
    kbo_r_count = 0
    pitcher_line_games = 0
    metric_games = 0
    with zipfile.ZipFile(sample) as archive:
        names = [name for name in archive.namelist() if name.lower().endswith('.json')]
        for name in names:
            game_count += 1
            data = json.loads(archive.read(name))
            result = data.get("result") or {}
            game = result.get("game") or {}
            relay = result.get("textRelayData") or {}
            if not game.get("gameId"):
                issues.append(f"{name}: result.game.gameId missing")
            if str(game.get("roundCode") or "").strip().lower() == "kbo_r":
                kbo_r_count += 1
            home_pitchers = ((relay.get("homeLineup") or {}).get("pitcher") or [])
            away_pitchers = ((relay.get("awayLineup") or {}).get("pitcher") or [])
            if home_pitchers and away_pitchers and all("er" in line and "inn" in line for line in home_pitchers + away_pitchers):
                pitcher_line_games += 1
            groups = relay.get("textRelays") or []
            if any(isinstance(group.get("metricOption"), dict) for group in groups if isinstance(group, dict)):
                metric_games += 1
    return {
        "gameCount": game_count,
        "kboRGameCount": kbo_r_count,
        "gamesWithFinalPitcherLines": pitcher_line_games,
        "gamesWithMetricOptions": metric_games,
        "issues": issues,
    }


def main() -> int:
    failures: list[str] = []
    schema = extract_schema()
    with tempfile.TemporaryDirectory() as temp_dir:
        database = Path(temp_dir) / "warehouse.db"
        connection = sqlite3.connect(database)
        try:
            connection.executescript(schema)
            columns = table_columns(connection)
            expected = {
                "Games", "Players", "GamePlayers", "PlateAppearances", "Pitches",
                "RunnerEvents", "PlayerChanges", "AdministrativeEvents", "BattingGameLines",
                "PitchingGameLines", "BatterGameStats", "PitcherGameStats", "ComputedCache",
                "LeagueConstants", "ParkFactors", "ParsedSources",
            }
            missing = sorted(expected - set(columns))
            if missing:
                failures.append(f"Missing expected relational tables: {missing}")
            forbidden_columns = [
                f"{table}.{column}"
                for table, names in columns.items()
                for column in names
                if column.lower() in {"normalizedjson", "rawjson", "sourcejson", "jsonblob"}
            ]
            if forbidden_columns:
                failures.append(f"Forbidden game JSON columns: {forbidden_columns}")
            insert_count, insert_failures = validate_insert_shapes(columns)
            failures.extend(insert_failures)
            sql_count, sql_skipped, sql_failures = validate_sql(connection)
            failures.extend(sql_failures)
            index_count = connection.execute(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'"
            ).fetchone()[0]
        finally:
            connection.close()

    failures.extend(validate_architecture())
    sample = validate_sample()
    failures.extend(sample["issues"])
    report = {
        "pass": not failures,
        "schemaTableCount": len(columns),
        "schemaIndexCount": index_count,
        "insertStatementsChecked": insert_count,
        "sqlStatementsCompiled": sql_count,
        "interpolatedSqlStatementsSkipped": sql_skipped,
        "sourceJsonColumns": 0,
        "sample": sample,
        "failures": failures,
        "note": "SQLite/schema/architecture validation only; dotnet build is still required on Windows.",
    }
    output = ROOT / "validation" / "relational-warehouse-validation.json"
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
