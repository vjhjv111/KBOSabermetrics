#!/usr/bin/env python3
"""Validate the seven supplied Naver KBO relay JSON files against phase-1 assumptions.

This is an independent raw-data reference check. It does not replace the C# CLI's
--validate-known-sample mode, which validates the actual normalized output.
"""
from __future__ import annotations

import argparse
import collections
import json
import math
import re
import sys
import zipfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

KNOWN_TYPES = {0, 1, 2, 7, 8, 13, 14, 23, 24, 99}
KNOWN_PITCH_RESULTS = {"B", "F", "H", "S", "T", "W"}
RUNNER_RE = re.compile(r"^(?P<from>[123])루주자\s+(?P<name>.+?)\s*:\s*(?P<action>.+)$")

EXPECTED_TOTALS = {
    "games": 7,
    "groups": 666,
    "events": 3646,
    "completed_pa": 522,
    "interrupted_pa": 1,
    "pre_plate_substitution": 16,
    "inning_markers": 120,
    "game_summaries": 7,
    "unknown_relay_groups": 0,
    "pitches": 1997,
    "pts_rows": 1996,
    "pts_matched": 1996,
    "pts_missing": 1,
    "runner_events": 193,
    "player_changes": 120,
    "administrative_events": 141,
    "duplicate_seqno_occurrences": 16,
    "first_state_pitcher_mismatches": 44,
    "last_event_not_batter_result": 117,
    "batting_side_title_mismatches": 0,
    "unrecognized_batter_results": 0,
    "unparsed_runner_events": 0,
    "unrecognized_administrative_events": 0,
    "unrecognized_player_changes": 0,
    "unknown_raw_event_types": 0,
    "unknown_pitch_result_codes": 0,
    "pts_calculation_failures": 0,
    "final_line_pa_mismatches": 0,
    "final_line_batting_stat_mismatches": 0,
}

EXPECTED_GAMES = {
    "20260626HTOB02026": (92, 537, 73, 0, 1, 17, 1, 302, 302, 0, 27, 16, 26, 1),
    "20260626KTSS02026": (97, 544, 74, 0, 5, 17, 1, 310, 310, 0, 25, 21, 16, 5),
    "20260626LGLT02026": (88, 437, 66, 1, 3, 17, 1, 233, 233, 0, 18, 15, 16, 3),
    "20260626WONC02026": (100, 535, 79, 0, 3, 17, 1, 275, 275, 0, 41, 22, 17, 3),
    "20260627HHSK02026": (99, 533, 80, 0, 0, 18, 1, 305, 305, 0, 22, 10, 16, 0),
    "20260627HTOB02026": (96, 542, 75, 0, 3, 17, 1, 286, 285, 1, 32, 23, 29, 3),
    "20260627KTSS02026": (94, 518, 75, 0, 1, 17, 1, 286, 286, 0, 28, 13, 21, 1),
}
EXPECTED_GAME_FIELDS = (
    "groups", "events", "completed_pa", "interrupted_pa", "pre_plate_substitution",
    "inning_markers", "game_summaries", "pitches", "pts_matched", "pts_missing",
    "runner_events", "player_changes", "administrative_events", "duplicate_seqno_occurrences",
)

@dataclass
class GameReport:
    game_id: str
    source: str
    groups: int = 0
    events: int = 0
    completed_pa: int = 0
    interrupted_pa: int = 0
    pre_plate_substitution: int = 0
    inning_markers: int = 0
    game_summaries: int = 0
    unknown_relay_groups: int = 0
    pitches: int = 0
    pts_rows: int = 0
    pts_matched: int = 0
    pts_missing: int = 0
    runner_events: int = 0
    player_changes: int = 0
    administrative_events: int = 0
    duplicate_seqno_occurrences: int = 0
    first_state_pitcher_mismatches: int = 0
    last_event_not_batter_result: int = 0
    batting_side_title_mismatches: int = 0
    unrecognized_batter_results: int = 0
    unparsed_runner_events: int = 0
    unrecognized_administrative_events: int = 0
    unrecognized_player_changes: int = 0
    unknown_raw_event_types: int = 0
    unknown_pitch_result_codes: int = 0
    pts_calculation_failures: int = 0
    final_line_pa_mismatches: int = 0
    final_line_batting_stat_mismatches: int = 0


def load_json_documents(path: Path) -> Iterable[tuple[str, dict[str, Any]]]:
    if path.is_dir():
        for file in sorted(path.rglob("*.json")):
            yield str(file), json.loads(file.read_text(encoding="utf-8"))
        return
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as archive:
            for name in sorted(n for n in archive.namelist() if n.lower().endswith(".json")):
                yield name, json.loads(archive.read(name).decode("utf-8"))
        return
    yield str(path), json.loads(path.read_text(encoding="utf-8"))


def classify_group(options: list[dict[str, Any]]) -> str:
    types = [item.get("type") for item in options]
    has_start = 8 in types
    has_pitch = 1 in types
    has_batter_result = any(value in (13, 23) for value in types)
    has_runner_result = any(value in (14, 24) for value in types)
    has_change = 2 in types
    if types and all(value == 0 for value in types):
        return "inning_markers"
    if types and all(value == 99 for value in types):
        return "game_summaries"
    if has_batter_result:
        return "completed_pa"
    last_out = safe_int((options[-1].get("currentGameState") or {}).get("out")) if options else None
    if has_start and has_pitch and has_runner_result and last_out == 3:
        return "interrupted_pa"
    if has_start and not has_pitch and has_change:
        return "pre_plate_substitution"
    if has_start and (has_pitch or has_runner_result or 7 in types):
        return "incomplete_pa"
    if types and all(value == 2 for value in types):
        return "player_change_only"
    if types and all(value == 7 for value in types):
        return "administrative_only"
    return "unknown_group"


def batter_result_recognized(text: str | None) -> bool:
    value = text_after_colon(text)
    tokens = (
        "자동 고의4구", "고의4구", "볼넷", "몸에 맞는 볼", "홈런", "3루타", "2루타",
        "번트안타", "내야안타", "1루타", "스트라이크 낫 아웃", "삼진 아웃",
        "희생플라이 아웃", "희생번트 아웃", "병살타 아웃", "실책으로 출루",
        "땅볼로 출루", "아웃",
    )
    return any(token in value for token in tokens)


def classify_batter_line_counts(text: str | None) -> collections.Counter[str]:
    """Mirror BatterResultClassifier's box-score flags for an independent cross-check."""
    value = text_after_colon(text)
    counts: collections.Counter[str] = collections.Counter(pa=1)

    if "자동 고의4구" in value or "고의4구" in value:
        counts.update(bb=1)
    elif "볼넷" in value:
        counts.update(bb=1)
    elif "몸에 맞는 볼" in value:
        counts.update(hbp=1)
    elif "홈런" in value:
        counts.update(ab=1, h=1, hr=1)
    elif any(token in value for token in ("3루타", "2루타", "번트안타", "내야안타", "1루타")):
        counts.update(ab=1, h=1)
    elif "스트라이크 낫 아웃" in value or "삼진 아웃" in value:
        counts.update(ab=1, so=1)
    elif "희생플라이 아웃" in value or "희생번트 아웃" in value:
        pass
    elif any(token in value for token in (
        "병살타 아웃", "실책으로 출루", "땅볼로 출루", "번트",
        "인필드플라이 아웃", "파울플라이 아웃", "라인드라이브 아웃",
        "땅볼 아웃", "플라이 아웃", "아웃",
    )):
        counts.update(ab=1)

    return counts


def administrative_recognized(text: str | None) -> bool:
    value = (text or "").strip()
    return any(token in value for token in (
        "퇴장", "피치클락", "비디오 판독", "비디오판독", "코칭스태프 마운드 방문",
        "포수 마운드 방문", "투수판 이탈",
    ))


def player_change_recognized(option: dict[str, Any]) -> bool:
    raw = option.get("playerChange") or {}
    raw_type = str(raw.get("type") or "").strip().lower()
    if raw_type == "substitution":
        incoming = raw.get("inPlayer") or {}
        outgoing = raw.get("outPlayer") or {}
        return bool(incoming.get("playerName") and outgoing.get("playerName"))
    if raw_type == "shift":
        player = raw.get("shiftPlayer") or {}
        return bool(player.get("playerName") and raw.get("shiftMessage"))
    text = str(option.get("text") or raw.get("liveText") or "").strip()
    return " : " in text and ("교체" in text or "수비위치 변경" in text)


def solve_at_plate(pts: dict[str, Any]) -> tuple[float, float, float] | None:
    keys = ("y0", "vy0", "ay", "crossPlateY", "x0", "vx0", "ax", "z0", "vz0", "az")
    if any(pts.get(key) is None for key in keys):
        return None
    y0, vy0, ay, plane = (float(pts[key]) for key in ("y0", "vy0", "ay", "crossPlateY"))
    a, b, c = 0.5 * ay, vy0, y0 - plane
    eps = 1e-12
    roots: list[float] = []
    if abs(a) < eps:
        if abs(b) < eps:
            return None
        roots = [-c / b]
    else:
        discriminant = b * b - 4.0 * a * c
        if discriminant < 0 or not math.isfinite(discriminant):
            return None
        square_root = math.sqrt(discriminant)
        roots = [(-b + square_root) / (2.0 * a), (-b - square_root) / (2.0 * a)]
    valid = [root for root in roots if root >= 0 and math.isfinite(root)]
    if not valid:
        return None
    time = min(valid)
    x = float(pts["x0"]) + float(pts["vx0"]) * time + 0.5 * float(pts["ax"]) * time * time
    z = float(pts["z0"]) + float(pts["vz0"]) * time + 0.5 * float(pts["az"]) * time * time
    return (time, x, z) if all(math.isfinite(value) for value in (time, x, z)) else None


def analyze(source: str, document: dict[str, Any]) -> GameReport:
    result = document.get("result") or {}
    relay = result.get("textRelayData") or {}
    game = result.get("game") or {}
    game_id = str(game.get("gameId") or relay.get("gameId") or Path(source).stem)
    report = GameReport(game_id=game_id, source=source)
    plays = list(reversed(relay.get("textRelays") or []))
    all_seqnos: list[int] = []
    parsed_batting: dict[str, collections.Counter[str]] = {
        "away": collections.Counter(),
        "home": collections.Counter(),
    }

    for play in plays:
        report.groups += 1
        options = play.get("textOptions") or []
        report.events += len(options)
        report.pts_rows += len(play.get("ptsOptions") or [])
        group_kind = classify_group(options)
        if hasattr(report, group_kind):
            setattr(report, group_kind, getattr(report, group_kind) + 1)
        elif group_kind == "unknown_group":
            report.unknown_relay_groups += 1

        title = str(play.get("title") or "")
        side = str(play.get("homeOrAway") or "")
        if ("회초" in title and side != "0") or ("회말" in title and side != "1"):
            report.batting_side_title_mismatches += 1

        types = [option.get("type") for option in options]
        report.unknown_raw_event_types += sum(value not in KNOWN_TYPES for value in types)
        all_seqnos.extend(option["seqno"] for option in options if option.get("seqno") is not None)

        batter_result_indices = [index for index, value in enumerate(types) if value in (13, 23)]
        if batter_result_indices and batter_result_indices[-1] != len(options) - 1:
            report.last_event_not_batter_result += 1

        first_pitch = next((option for option in options if option.get("type") == 1), None)
        if first_pitch is not None:
            first_state_pitcher = ((options[0].get("currentGameState") or {}).get("pitcher")
                                   if options else None)
            actual_pitcher = (first_pitch.get("currentGameState") or {}).get("pitcher")
            if first_state_pitcher != actual_pitcher:
                report.first_state_pitcher_mismatches += 1

        pts_by_id = {str(item.get("pitchId")): item for item in (play.get("ptsOptions") or [])
                     if item.get("pitchId") is not None}
        for option in options:
            raw_type = option.get("type")
            if raw_type == 1:
                report.pitches += 1
                code = str(option.get("pitchResult") or "").upper()
                if code not in KNOWN_PITCH_RESULTS:
                    report.unknown_pitch_result_codes += 1
                pts = pts_by_id.get(str(option.get("ptsPitchId")))
                if pts is None:
                    report.pts_missing += 1
                else:
                    report.pts_matched += 1
                    if solve_at_plate(pts) is None:
                        report.pts_calculation_failures += 1
            elif raw_type in (13, 23):
                if not batter_result_recognized(option.get("text")):
                    report.unrecognized_batter_results += 1
                batting_key = "away" if side == "0" else "home" if side == "1" else None
                if batting_key is not None:
                    parsed_batting[batting_key].update(classify_batter_line_counts(option.get("text")))
            elif raw_type in (14, 24):
                report.runner_events += 1
                if RUNNER_RE.match(str(option.get("text") or "").strip()) is None:
                    report.unparsed_runner_events += 1
            elif raw_type == 2:
                report.player_changes += 1
                if not player_change_recognized(option):
                    report.unrecognized_player_changes += 1
            elif raw_type == 7:
                report.administrative_events += 1
                if not administrative_recognized(option.get("text")):
                    report.unrecognized_administrative_events += 1

    seq_counts = collections.Counter(all_seqnos)
    report.duplicate_seqno_occurrences = sum(count - 1 for count in seq_counts.values() if count > 1)

    final_line_pa = sum(
        safe_int(player.get("pa")) or 0
        for side in ("awayLineup", "homeLineup")
        for player in ((relay.get(side) or {}).get("batter") or [])
    )
    if str(game.get("statusCode") or "").upper() == "RESULT":
        if final_line_pa != report.completed_pa:
            report.final_line_pa_mismatches += 1

        for batting_key, lineup_key in (("away", "awayLineup"), ("home", "homeLineup")):
            final_counts: collections.Counter[str] = collections.Counter()
            final_field_names = {"pa": "pa", "ab": "ab", "h": "hit", "hr": "hr",
                                 "bb": "bb", "hbp": "hbp", "so": "so"}
            for player in ((relay.get(lineup_key) or {}).get("batter") or []):
                for stat, field_name in final_field_names.items():
                    final_counts[stat] += safe_int(player.get(field_name)) or 0

            for stat in ("pa", "ab", "h", "hr", "bb", "hbp", "so"):
                if parsed_batting[batting_key][stat] != final_counts[stat]:
                    report.final_line_batting_stat_mismatches += 1
    return report


def safe_int(value: Any) -> int | None:
    try:
        return int(value) if value is not None and str(value).strip() else None
    except (TypeError, ValueError):
        return None


def text_after_colon(text: str | None) -> str:
    value = (text or "").strip()
    return value.split(" : ", 1)[1].strip() if " : " in value else value


def sum_reports(reports: list[GameReport]) -> dict[str, int]:
    totals: dict[str, int] = {"games": len(reports)}
    for field in GameReport.__dataclass_fields__:
        if field in {"game_id", "source"}:
            continue
        totals[field] = sum(int(getattr(report, field)) for report in reports)
    return totals


def compare_expected(reports: list[GameReport], totals: dict[str, int]) -> list[str]:
    failures: list[str] = []
    for field, expected in EXPECTED_TOTALS.items():
        actual = totals.get(field)
        if actual != expected:
            failures.append(f"aggregate {field}: expected {expected}, actual {actual}")

    by_id = {report.game_id: report for report in reports}
    for game_id, values in EXPECTED_GAMES.items():
        report = by_id.get(game_id)
        if report is None:
            failures.append(f"missing game {game_id}")
            continue
        for field, expected in zip(EXPECTED_GAME_FIELDS, values):
            actual = int(getattr(report, field))
            if actual != expected:
                failures.append(f"{game_id} {field}: expected {expected}, actual {actual}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path, help="2026.zip, a JSON directory, or one JSON file")
    parser.add_argument("--output", type=Path, help="optional JSON report path")
    args = parser.parse_args()

    reports = [analyze(source, document) for source, document in load_json_documents(args.input)]
    totals = sum_reports(reports)
    failures = compare_expected(reports, totals)
    payload = {
        "input": str(args.input.resolve()),
        "pass": not failures,
        "expectedTotals": EXPECTED_TOTALS,
        "actualTotals": totals,
        "games": [asdict(report) for report in reports],
        "failures": failures,
    }
    text = json.dumps(payload, ensure_ascii=False, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
