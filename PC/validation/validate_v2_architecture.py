#!/usr/bin/env python3
from __future__ import annotations

import json
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
FAILURES: list[str] = []
CHECKS: list[dict[str, object]] = []


def check(name: str, condition: bool, detail: str) -> None:
    CHECKS.append({"name": name, "pass": bool(condition), "detail": detail})
    if not condition:
        FAILURES.append(f"{name}: {detail}")


def text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def strip_comments_and_strings(source: str) -> str:
    """Mask C# comments and string/character literals while preserving newlines.

    This deliberately treats interpolated strings as opaque. It supports regular,
    verbatim and C# 11 raw strings, which is enough for delimiter and declaration
    checks without being confused by SQL stored inside raw strings.
    """
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

        # A quote can be preceded by $, @, $@ or @$ without changing how far
        # backward we need to mask. Raw strings use three or more quotes.
        if ch == '"':
            quote_count = 1
            while i + quote_count < length and chars[i + quote_count] == '"':
                quote_count += 1

            prefix_start = i
            while prefix_start > 0 and chars[prefix_start - 1] in "$@":
                prefix_start -= 1

            if quote_count >= 3:
                start = prefix_start
                delimiter = '"' * quote_count
                i += quote_count
                closing = source.find(delimiter, i)
                i = length if closing < 0 else closing + quote_count
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


def validate_projects() -> None:
    expected = {
        "NaverRelay.Parser",
        "NaverRelay.Application",
        "NaverRelay.Infrastructure.Sqlite",
        "NaverRelay.Gui",
        "NaverRelay.Cli",
        "NaverRelay.Api",
    }
    projects = {p.parent.name for p in SRC.glob("*/*.csproj")}
    check("project-set", projects == expected, f"expected={sorted(expected)}, actual={sorted(projects)}")

    for project in SRC.glob("*/*.csproj"):
        try:
            ET.parse(project)
            ok = True
        except Exception as exc:  # pragma: no cover - diagnostic path
            ok = False
            FAILURES.append(f"Invalid XML {project}: {exc}")
        CHECKS.append({"name": f"xml:{project.parent.name}", "pass": ok, "detail": str(project.relative_to(ROOT))})

    solution = text(ROOT / "NaverSabermetrics.V2.sln")
    missing = [str(p.relative_to(ROOT)).replace("/", "\\") for p in SRC.glob("*/*.csproj") if str(p.relative_to(ROOT)).replace("/", "\\") not in solution]
    check("solution-project-paths", not missing, f"missing={missing}")

    gui_project = text(SRC / "NaverRelay.Gui/NaverRelay.Gui.csproj")
    check("desktop-references-infrastructure", "NaverRelay.Infrastructure.Sqlite" in gui_project, "Desktop must reference relational SQLite infrastructure")
    check("desktop-no-sqlite-package", "Microsoft.Data.Sqlite" not in gui_project, "SQLite package belongs only to Infrastructure")

    api_project = text(SRC / "NaverRelay.Api/NaverRelay.Api.csproj")
    check("api-no-winforms-reference", "NaverRelay.Gui" not in api_project, "Web API must not depend on WinForms")
    check("api-references-application", "NaverRelay.Application" in api_project, "API uses shared application contracts")
    check("api-references-infrastructure", "NaverRelay.Infrastructure.Sqlite" in api_project, "API uses same relational DB implementation")


def validate_architecture() -> None:
    infra_files = list((SRC / "NaverRelay.Infrastructure.Sqlite").rglob("*.cs"))
    app_files = list((SRC / "NaverRelay.Application").rglob("*.cs"))
    infra_text = "\n".join(text(p) for p in infra_files)
    app_text = "\n".join(text(p) for p in app_files)
    all_source = "\n".join(text(p) for p in SRC.rglob("*.cs"))

    check("application-no-gui-dependency", "NaverRelay.Gui" not in app_text, "Application contracts must be UI independent")
    check("infrastructure-no-gui-dependency", "NaverRelay.Gui" not in infra_text, "SQLite infrastructure must be usable by Desktop and API")
    check("v2-database-name", 'sabermetrics_v2.db' in infra_text, "V2 must use a DB isolated from all legacy files")
    check("exact-regular-season", "LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'" in infra_text, "Regular-season and league constants use exact kbo_r")
    check("application-run-qualified", "System.Windows.Forms.Application.Run" in text(SRC / "NaverRelay.Gui/Program.cs"), "Avoid Application namespace collision")
    check("no-invalid-escape-shortcut", "ShortcutKeys = Keys.Escape" not in all_source, "Escape is handled through ProcessCmdKey, not ToolStrip shortcut")

    designer = text(SRC / "NaverRelay.Gui/MainForm.Designer.cs")
    check("no-designer-splitter-distance", ".SplitterDistance =" not in designer, "Splitter distance is applied after layout")

    db = text(SRC / "NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.cs")
    check("schema-version-3", 'WarehouseSchemaVersion = "3"' in db, "Current V2 relational schema")
    check("custom-db-path-constructor", "DatabaseCacheService(string? databasePath = null)" in db, "Desktop/API can point to the same DB")

    analytics = text(SRC / "NaverRelay.Infrastructure.Sqlite/DatabaseAnalyticsService.cs")
    check("nullable-fipr9-explicit", "double? fipR9" in analytics, "Prevent CS0173 from double/null conditional")
    check("analytics-interface", "IAnalyticsQueryService" in analytics, "Analytics implementation is exposed through an application contract")

    api = text(SRC / "NaverRelay.Api/Program.cs")
    for route in ["/api/health", "/api/catalog", "/api/games", "/api/stats/batters", "/api/stats/pitchers", "/api/players/search", "/api/players/{pcode}"]:
        check(f"api-route:{route}", route in api, f"route {route}")


def validate_csharp_structure() -> None:
    cs_files = list(SRC.rglob("*.cs"))
    brace_failures: list[str] = []
    ternary_hazards: list[str] = []

    for path in cs_files:
        source = text(path)
        clean = strip_comments_and_strings(source)
        counts = {ch: clean.count(ch) for ch in "{}[]()"}
        if counts["{"] != counts["}"] or counts["["] != counts["]"] or counts["("] != counts[")"]:
            brace_failures.append(f"{path.relative_to(ROOT)}:{counts}")

        lines = source.splitlines()
        index = 0
        while index < len(lines):
            line = lines[index]
            if re.search(r"\bvar\s+[A-Za-z_]\w*\s*=", line):
                statement = line.strip()
                end = index
                while ";" not in statement and end + 1 < len(lines) and end - index < 24:
                    end += 1
                    statement += " " + lines[end].strip()
                # String/reference conditionals are valid; flag numeric/Value/Math conditionals only.
                if re.search(r"\?[^:;]*(?:\.Value\b|\bMath\.|\d+\.\d+|/|\*)[^:;]*:\s*null\s*;", statement):
                    ternary_hazards.append(f"{path.relative_to(ROOT)}:{index + 1}:{statement[:240]}")
                index = end
            index += 1

    check("balanced-csharp-delimiters", not brace_failures, f"failures={brace_failures[:10]}")
    check("no-var-value-null-conditional", not ternary_hazards, f"hazards={ternary_hazards[:10]}")

    # Unique custom type namespace resolution check after project split.
    type_map: dict[str, list[tuple[str, Path, str]]] = defaultdict(list)
    file_info: dict[Path, tuple[str, set[str], str, set[str], str]] = {}
    for path in cs_files:
        raw = text(path)
        clean = strip_comments_and_strings(raw)
        namespace_match = re.search(r"\bnamespace\s+([A-Za-z_][\w.]*)\s*[;{]", clean)
        namespace = namespace_match.group(1) if namespace_match else ""
        usings = set(re.findall(r"^\s*using\s+([A-Za-z_][\w.]*)\s*;", clean, flags=re.M))
        declarations: set[str] = set()
        project = path.relative_to(SRC).parts[0]
        for match in re.finditer(r"\b(public|internal|private|protected|file)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*\s*(?:class|interface|enum|record(?:\s+struct)?|struct)\s+([A-Za-z_]\w*)", clean):
            access = match.group(1) or "internal"
            name = match.group(2)
            declarations.add(name)
            type_map[name].append((namespace, path, access))
        file_info[path] = (namespace, usings, clean, declarations, project)

    namespace_issues: list[str] = []
    internal_cross_project: list[str] = []
    for path, (namespace, usings, clean, declarations, project) in file_info.items():
        for name, locations in type_map.items():
            if name in declarations or len(locations) != 1:
                continue
            target_namespace, target_path, target_access = locations[0]
            if not re.search(r"(?<![\w.])" + re.escape(name) + r"\b", clean):
                continue
            if target_namespace != namespace and target_namespace not in usings:
                namespace_issues.append(f"{path.relative_to(ROOT)} uses {name} from {target_namespace} without using")
            target_project = target_path.relative_to(SRC).parts[0]
            if target_project != project and target_access != "public":
                internal_cross_project.append(f"{path.relative_to(ROOT)} uses non-public {name} from {target_project}")

    # Known enum member 'PlayerChange' is not a type reference in NormalizedEnums.cs.
    namespace_issues = [item for item in namespace_issues if not ("NormalizedEnums.cs" in item and "PlayerChange" in item)]
    check("custom-type-namespace-resolution", not namespace_issues, f"issues={namespace_issues[:20]}")
    check("no-cross-project-internal-types", not internal_cross_project, f"issues={internal_cross_project[:20]}")


def validate_schema() -> None:
    service = text(SRC / "NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.cs")
    match = re.search(r"private const string SchemaSql = \"\"\"(.*?)\"\"\";", service, flags=re.S)
    if not match:
        check("schema-extracted", False, "SchemaSql raw string not found")
        return
    schema = match.group(1)
    forbidden_columns = re.findall(r"\b(?:NormalizedJson|RawJson|SourceJson|JsonBlob)\b", schema, flags=re.I)
    check("no-json-blob-columns", not forbidden_columns, f"forbidden schema columns={forbidden_columns}")
    try:
        connection = sqlite3.connect(":memory:")
        connection.executescript(schema)
        tables = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")]
        indexes = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='index'")]
        connection.close()
        check("schema-sql-compiles", True, f"tables={len(tables)}, indexes={len(indexes)}")
        check("core-relational-tables", all(name in tables for name in [
            "Games", "Players", "GamePlayers", "PlateAppearances", "Pitches",
            "RunnerEvents", "PlayerChanges", "BattingGameLines", "PitchingGameLines",
            "BatterGameStats", "PitcherGameStats", "ComputedCache", "ParsedSources",
        ]), f"tables={tables}")
    except Exception as exc:
        check("schema-sql-compiles", False, str(exc))


def main() -> int:
    validate_projects()
    validate_architecture()
    validate_csharp_structure()
    validate_schema()
    result = {
        "projectRoot": str(ROOT),
        "pass": not FAILURES,
        "csharpFileCount": len(list(SRC.rglob("*.cs"))),
        "projectFileCount": len(list(SRC.rglob("*.csproj"))),
        "checks": CHECKS,
        "failures": FAILURES,
        "note": "Static architecture/schema validation. A Windows dotnet build is still the final compiler check.",
    }
    output = ROOT / "validation" / "v2-architecture-validation.json"
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
