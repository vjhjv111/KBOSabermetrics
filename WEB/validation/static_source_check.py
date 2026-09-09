#!/usr/bin/env python3
"""Dependency-free structural checks for the phase-1 C# source tree.

This is not a replacement for `dotnet build`; it catches damaged files, unbalanced
source delimiters, missing project references, and regression of the parser's
most important invariants.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path, PureWindowsPath


def strip_comments_and_literals(source: str) -> str:
    """Mask C# comments and literals, including C# 11 raw strings."""
    chars = list(source)
    result = list(source)
    length = len(chars)

    def mask(start: int, end: int) -> None:
        for pos in range(start, min(end, length)):
            if chars[pos] != "\n":
                result[pos] = " "

    i = 0
    while i < length:
        ch = chars[i]
        nxt = chars[i + 1] if i + 1 < length else ""
        if ch == "/" and nxt == "/":
            start = i
            i += 2
            while i < length and chars[i] != "\n":
                i += 1
            mask(start, i)
            continue
        if ch == "/" and nxt == "*":
            start = i
            i += 2
            while i + 1 < length and not (chars[i] == "*" and chars[i + 1] == "/"):
                i += 1
            i = min(length, i + 2)
            mask(start, i)
            continue
        if ch == "'":
            start = i
            i += 1
            while i < length:
                if chars[i] == "\\":
                    i += 2
                    continue
                if chars[i] == "'":
                    i += 1
                    break
                i += 1
            mask(start, i)
            continue
        if ch == '"':
            quote_count = 1
            while i + quote_count < length and chars[i + quote_count] == '"':
                quote_count += 1
            prefix_start = i
            while prefix_start > 0 and chars[prefix_start - 1] in "$@":
                prefix_start -= 1
            if quote_count >= 3:
                delimiter = '"' * quote_count
                start = prefix_start
                i += quote_count
                closing = source.find(delimiter, i)
                if closing < 0:
                    raise ValueError("unterminated raw string literal")
                i = closing + quote_count
                mask(start, i)
                continue
            is_verbatim = "@" in source[prefix_start:i]
            start = prefix_start
            i += 1
            while i < length:
                if is_verbatim:
                    if chars[i] == '"' and i + 1 < length and chars[i + 1] == '"':
                        i += 2
                        continue
                    if chars[i] == '"':
                        i += 1
                        break
                    i += 1
                else:
                    if chars[i] == "\\":
                        i += 2
                        continue
                    if chars[i] == '"':
                        i += 1
                        break
                    i += 1
            mask(start, i)
            continue
        i += 1
    return "".join(result)



def find_matching_brace(source: str, opening_index: int) -> int | None:
    depth = 0
    for index in range(opening_index, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return index
    return None


def extract_class_properties(cs_files: list[Path]) -> dict[str, set[str]]:
    """Build a small property index for classes declared in this source tree."""
    classes: dict[str, set[str]] = {}
    class_pattern = re.compile(
        r"\b(?:public|internal|private|protected)?\s*"
        r"(?:static\s+)?(?:sealed\s+)?(?:partial\s+)?class\s+(\w+)"
    )
    property_pattern = re.compile(
        r"\bpublic\s+[\w?.<>,\[\]\s]+\s+(\w+)"
        r"\s*\{\s*get\s*;\s*(?:set|init)\s*;"
    )

    for path in cs_files:
        stripped = strip_comments_and_literals(path.read_text(encoding="utf-8"))
        for match in class_pattern.finditer(stripped):
            opening = stripped.find("{", match.end())
            if opening < 0:
                continue
            closing = find_matching_brace(stripped, opening)
            if closing is None:
                continue
            body = stripped[opening + 1:closing]
            classes[match.group(1)] = set(property_pattern.findall(body))
    return classes


def check_object_initializers(
    cs_files: list[Path],
    class_properties: dict[str, set[str]],
) -> tuple[list[str], int]:
    """Check assignments in object initializers against locally declared properties."""
    failures: list[str] = []
    assignment_count = 0
    initializer_pattern = re.compile(
        r"\bnew\s+(\w+)\s*(?:\([^;{}]*\))?\s*\{"
    )

    for path in cs_files:
        stripped = strip_comments_and_literals(path.read_text(encoding="utf-8"))
        for match in initializer_pattern.finditer(stripped):
            class_name = match.group(1)
            properties = class_properties.get(class_name)
            if properties is None:
                continue

            opening = stripped.find("{", match.start())
            closing = find_matching_brace(stripped, opening)
            if opening < 0 or closing is None:
                continue
            body = stripped[opening + 1:closing]

            depth = 0
            index = 0
            while index < len(body):
                char = body[index]
                if char in "({[":
                    depth += 1
                elif char in ")}]":
                    depth -= 1
                elif depth == 0 and (char.isalpha() or char == "_"):
                    assignment = re.match(r"(\w+)\s*=(?!=)", body[index:])
                    if assignment:
                        property_name = assignment.group(1)
                        assignment_count += 1
                        if property_name not in properties:
                            failures.append(
                                f"{path}: object initializer assigns unknown property "
                                f"{class_name}.{property_name}"
                            )
                        index += assignment.end() - 1
                index += 1
    return failures, assignment_count


def extract_enum_members(cs_files: list[Path]) -> dict[str, set[str]]:
    enums: dict[str, set[str]] = {}
    enum_pattern = re.compile(r"\benum\s+(\w+)\s*\{")
    for path in cs_files:
        stripped = strip_comments_and_literals(path.read_text(encoding="utf-8"))
        for match in enum_pattern.finditer(stripped):
            opening = stripped.find("{", match.end() - 1)
            closing = find_matching_brace(stripped, opening)
            if opening < 0 or closing is None:
                continue
            body = stripped[opening + 1:closing]
            members: set[str] = set()
            for part in body.split(","):
                name_match = re.match(r"\s*(\w+)", part)
                if name_match:
                    members.add(name_match.group(1))
            enums[match.group(1)] = members
    return enums


def check_enum_references(
    cs_files: list[Path],
    enum_members: dict[str, set[str]],
) -> tuple[list[str], int]:
    failures: list[str] = []
    reference_count = 0
    if not enum_members:
        return failures, reference_count
    enum_names = "|".join(re.escape(name) for name in sorted(enum_members, key=len, reverse=True))
    reference_pattern = re.compile(rf"\b(?P<enum>{enum_names})\.(?P<member>\w+)")
    for path in cs_files:
        stripped = strip_comments_and_literals(path.read_text(encoding="utf-8"))
        for match in reference_pattern.finditer(stripped):
            reference_count += 1
            enum_name = match.group("enum")
            member_name = match.group("member")
            if member_name not in enum_members[enum_name]:
                failures.append(
                    f"{path}: unknown enum member {enum_name}.{member_name}"
                )
    return failures, reference_count


def check_delimiters(path: Path) -> list[str]:
    failures: list[str] = []
    try:
        source = path.read_text(encoding="utf-8")
        stripped = strip_comments_and_literals(source)
    except Exception as exc:  # noqa: BLE001
        return [f"{path}: cannot read/lex source: {exc}"]

    openings = {"(": ")", "[": "]", "{": "}"}
    closings = {value: key for key, value in openings.items()}
    stack: list[tuple[str, int, int]] = []
    line = 1
    column = 0
    for char in stripped:
        if char == "\n":
            line += 1
            column = 0
            continue
        column += 1
        if char in openings:
            stack.append((char, line, column))
        elif char in closings:
            if not stack:
                failures.append(f"{path}:{line}:{column}: unexpected '{char}'")
                continue
            opening, open_line, open_column = stack.pop()
            if openings[opening] != char:
                failures.append(
                    f"{path}:{line}:{column}: '{char}' closes '{opening}' from "
                    f"{open_line}:{open_column}"
                )
    for opening, open_line, open_column in stack:
        failures.append(f"{path}:{open_line}:{open_column}: unclosed '{opening}'")
    return failures


def parse_solution_projects(solution: Path) -> list[Path]:
    projects: list[Path] = []
    pattern = re.compile(r'^Project\("[^"]+"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"', re.M)
    for match in pattern.finditer(solution.read_text(encoding="utf-8")):
        relative = Path(*PureWindowsPath(match.group(1)).parts)
        projects.append((solution.parent / relative).resolve())
    return projects


def require_contains(path: Path, snippets: list[str], failures: list[str]) -> None:
    text = path.read_text(encoding="utf-8")
    for snippet in snippets:
        if snippet not in text:
            failures.append(f"{path}: required parser invariant is missing: {snippet!r}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("project_root", nargs="?", default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    root = Path(args.project_root).resolve()
    failures: list[str] = []

    cs_files = sorted(root.rglob("*.cs"))
    if not cs_files:
        failures.append("No C# source files found.")
    for path in cs_files:
        failures.extend(check_delimiters(path))

    class_properties = extract_class_properties(cs_files)
    initializer_failures, initializer_assignment_count = check_object_initializers(
        cs_files, class_properties
    )
    failures.extend(initializer_failures)

    enum_members = extract_enum_members(cs_files)
    enum_failures, enum_reference_count = check_enum_references(cs_files, enum_members)
    failures.extend(enum_failures)

    csproj_files = sorted(root.rglob("*.csproj"))
    if not csproj_files:
        failures.append("No .csproj files found.")
    for path in csproj_files:
        try:
            tree = ET.parse(path)
            for reference in tree.findall(".//ProjectReference"):
                include = reference.get("Include")
                if not include:
                    failures.append(f"{path}: ProjectReference without Include")
                    continue
                relative = Path(*PureWindowsPath(include).parts)
                target = (path.parent / relative).resolve()
                if not target.exists():
                    failures.append(f"{path}: ProjectReference target does not exist: {target}")
        except ET.ParseError as exc:
            failures.append(f"{path}: invalid XML: {exc}")

    solution = root / "NaverSabermetrics.sln"
    if not solution.exists():
        failures.append(f"Missing solution file: {solution}")
    else:
        projects = parse_solution_projects(solution)
        if not projects:
            failures.append("The solution does not reference any C# projects.")
        for project in projects:
            if not project.exists():
                failures.append(f"Solution project path does not exist: {project}")

    parser_file = root / "src/NaverRelay.Parser/Parsing/RelayParser.cs"
    utility_file = root / "src/NaverRelay.Parser/Parsing/ParserUtilities.cs"
    context_file = root / "src/NaverRelay.Parser/Parsing/ParsingContext.cs"
    pitch_file = root / "src/NaverRelay.Parser/Parsing/PitchHelpers.cs"
    model_file = root / "src/NaverRelay.Parser/Parsing/NormalizedModels.cs"
    consistency_file = root / "src/NaverRelay.Parser/Parsing/GameConsistencyValidator.cs"
    known_sample_file = root / "src/NaverRelay.Cli/KnownSampleValidator.cs"
    gui_project_file = root / "src/NaverRelay.Gui/NaverRelay.Gui.csproj"
    gui_main_file = root / "src/NaverRelay.Gui/MainForm.cs"
    gui_designer_file = root / "src/NaverRelay.Gui/MainForm.Designer.cs"
    gui_sample_file = root / "SampleData/2026.zip"
    for path in (parser_file, utility_file, context_file, pitch_file, model_file,
                 consistency_file, known_sample_file, gui_project_file, gui_main_file,
                 gui_designer_file, gui_sample_file):
        if not path.exists():
            failures.append(f"Missing required source file: {path}")

    if parser_file.exists():
        require_contains(parser_file, [
            "pair.Raw.Type == 13 || pair.Raw.Type == 23",
            "NormalizedEventType.RunnerResult",
            "PlayerChangeParser.Parse",
            "PitchTrajectoryCalculator.TryCalculateAtPlate",
            "GameConsistencyValidator.Validate",
            '"UNKNOWN_RELAY_GROUP"',
            'Code.StartsWith("FINAL_LINE_", StringComparison.Ordinal)',
        ], failures)
    if utility_file.exists():
        require_contains(utility_file, [
            '"0" => TeamSide.Away',
            '"1" => TeamSide.Home',
            "13 or 23 => NormalizedEventType.BatterResult",
            "14 or 24 => NormalizedEventType.RunnerResult",
        ], failures)
    if context_file.exists():
        require_contains(context_file, [
            "GetActivePlayer(battingSide, firstSlot)",
            "FirstBaseSlot = firstSlot",
            "ApplyPlayerChange",
        ], failures)
    if pitch_file.exists():
        require_contains(pitch_file, [
            "pts.Y0.Value - pts.CrossPlateY.Value",
            "CalculatedCrossPlateZ",
            "TrySmallestNonNegativeRoot",
        ], failures)
    if model_file.exists():
        require_contains(model_file, [
            "public List<RunnerEvent> RunnerEvents",
            "public List<PlayerChangeEvent> PlayerChanges",
            "public List<AdministrativeEvent> AdministrativeEvents",
            "public int FinalLineBattingMismatchCount",
        ], failures)
    if consistency_file.exists():
        require_contains(consistency_file, [
            '"FINAL_LINE_PA_MISMATCH"',
            '"FINAL_LINE_AB_MISMATCH"',
            '"FINAL_LINE_H_MISMATCH"',
            '"FINAL_LINE_HR_MISMATCH"',
            '"FINAL_LINE_BB_MISMATCH"',
            '"FINAL_LINE_HBP_MISMATCH"',
            '"FINAL_LINE_SO_MISMATCH"',
        ], failures)
    if known_sample_file.exists():
        require_contains(known_sample_file, [
            '"aggregate final batting line mismatches"',
            "s.FinalLineBattingMismatchCount",
        ], failures)
    if gui_project_file.exists():
        require_contains(gui_project_file, [
            "<TargetFramework>net8.0-windows</TargetFramework>",
            "<UseWindowsForms>true</UseWindowsForms>",
            "NaverRelay.Parser.csproj",
            "SampleData\\2026.zip",
        ], failures)
    if gui_main_file.exists():
        require_contains(gui_main_file, [
            "InputDiscoveryService.DiscoverAsync",
            "ParsingWorkflowService.RunAsync",
            "CsvExporter.ExportAsync",
            "KnownSampleValidationService.Validate",
            "EnsureReadIndexesAsync",
            "GetGameHeadersAsync",
            "RefreshCurrentViewAsync",
            "GetPlateAppearancePageAsync",
            "GetPitchPageAsync",
            "DatabasePlayerPageService",
            "RawPageSize = 5_000",
        ], failures)
        main_text = gui_main_file.read_text(encoding="utf-8")
        for forbidden in ("LoadAllGamesAsync", "_games.AddRange(cachedGames)", "NormalizedOutputWriter.SaveAllAsync(_games"):
            if forbidden in main_text:
                failures.append(f"{gui_main_file}: forbidden eager-loading pattern remains: {forbidden!r}")
    if gui_designer_file.exists():
        require_contains(gui_designer_file, [
            'new TabPage("타석")',
            'new TabPage("투구")',
            'new TabPage("주루")',
            'new TabPage("진단")',
            'new TabPage("SQLite DB")',
        ], failures)


    warehouse_service = root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.cs"
    warehouse_import = root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.Import.cs"
    warehouse_query = root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.Query.cs"
    if warehouse_service.exists():
        require_contains(warehouse_service, [
            'sabermetrics_v2.db',
            'CREATE TABLE IF NOT EXISTS PlateAppearances',
            'CREATE TABLE IF NOT EXISTS Pitches',
            'CREATE TABLE IF NOT EXISTS BatterGameStats',
            'CREATE TABLE IF NOT EXISTS PitcherGameStats',
            'StorageMode',
            'RelationalWarehouse',
        ], failures)
        service_text = warehouse_service.read_text(encoding="utf-8")
        if 'NormalizedJson' in service_text and 'Games.NormalizedJson 같은 대형 JSON 열은 만들지 않습니다.' not in service_text:
            failures.append(f"{warehouse_service}: NormalizedJson storage remains")
    else:
        failures.append(f"Missing relational warehouse service: {warehouse_service}")
    if warehouse_import.exists():
        require_contains(warehouse_import, [
            'InsertPlateAppearancesAsync',
            'InsertPitchesAsync',
            'InsertBatterGameStatsAsync',
            'InsertPitcherGameStatsAsync',
            'UpsertParsedSourceAsync',
        ], failures)
    if warehouse_query.exists():
        require_contains(warehouse_query, [
            'GetAggregateDataAsync',
            'FROM BatterGameStats',
            'FROM PitcherGameStats',
        ], failures)

    active_gui_sources = [
        root / "src/NaverRelay.Gui/MainForm.cs",
        root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseAnalyticsService.cs",
        root / "src/NaverRelay.Infrastructure.Sqlite/DatabasePlayerPageService.cs",
        root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.Query.cs",
        root / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.Raw.cs",
    ]
    for path in active_gui_sources:
        if not path.exists():
            failures.append(f"Missing DB-only query source: {path}")
            continue
        text = path.read_text(encoding="utf-8")
        for forbidden in ("JsonSerializer.Deserialize<NormalizedGame>", "RelayParser.ParseJson(", "LoadPlayerGamesAsync"):
            if forbidden in text:
                failures.append(f"{path}: active query path still uses source JSON/game deserialization: {forbidden!r}")

    obsolete_memory_service = root / "src/NaverRelay.Gui/Services/PlayerPageService.cs"
    if obsolete_memory_service.exists():
        failures.append(f"Obsolete in-memory player service remains: {obsolete_memory_service}")

    report = {
        "projectRoot": str(root),
        "pass": not failures,
        "csharpFileCount": len(cs_files),
        "projectFileCount": len(csproj_files),
        "objectInitializerAssignmentCount": initializer_assignment_count,
        "enumReferenceCount": enum_reference_count,
        "failures": failures,
        "note": "Structural source check only; run dotnet build for compiler validation.",
    }
    text = json.dumps(report, ensure_ascii=False, indent=2)
    print(text)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
