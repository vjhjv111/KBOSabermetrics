#!/usr/bin/env python3
"""Dependency-free structural and formula checks for KBO pitcher WAR v3."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def require(text: str, needle: str, failures: list[str], name: str) -> None:
    if needle not in text:
        failures.append(f"missing {name}: {needle}")


def main() -> int:
    failures: list[str] = []
    math_source = (ROOT / "src/NaverRelay.Infrastructure.Sqlite/KboPitcherWarMath.cs").read_text(encoding="utf-8")
    calibration_source = (ROOT / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.PitcherWarCalibration.cs").read_text(encoding="utf-8")
    analytics_source = (ROOT / "src/NaverRelay.Infrastructure.Sqlite/DatabaseAnalyticsService.cs").read_text(encoding="utf-8")
    formula_source = (ROOT / "src/NaverRelay.Gui/FormulaViewControl.cs").read_text(encoding="utf-8")
    constants_source = (ROOT / "src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.Analytics.cs").read_text(encoding="utf-8")

    required_math = [
        "DefaultReplacementWinningPercentage = 0.294",
        "DefaultPitcherWarShare = 0.43",
        "DefaultBlendFipWeight = 0.70",
        "DefaultBlendRa9Weight = 0.30",
        "DynamicRunsPerWin",
        "LeverageMultiplier",
        "ComputeTargetPitcherWar",
    ]
    for item in required_math:
        require(math_source, item, failures, "math")

    for item in [
        "BuildPitcherWarCalibrationAsync",
        "SelectReplacementPool",
        "EstimateReplacementRate",
        "FipWarPerInning",
        "Ra9WarPerInning",
        "StarterReplacementFipMinus",
        "RelieverReplacementFipMinus",
    ]:
        require(calibration_source, item, failures, "calibration")

    for item in [
        "WarPerInningCorrection = calibration.FipWarPerInning",
        "Ra9WarPerInningCorrection = calibration.Ra9WarPerInning",
        "BlendWar = blendWar",
        "StarterReplacementFipMinus",
        "RelieverReplacementFipMinus",
    ]:
        require(analytics_source, item, failures, "analytics mapping")

    for item in [
        '"KBO 투수 대체수준*"',
        '"KBO WARIP*"',
        '"KBO fWAR v3*"',
        '"KBO RA9-WAR*"',
        '"Blend WAR 70/30*"',
    ]:
        require(formula_source, item, failures, "formula table")

    for item in [
        'Row("KBO 투수 목표 WAR"',
        'Row("KBO fWAR 목표 달성률(보정 전)"',
        'Row("KBO fWAR WARIP"',
        'Row("KBO 선발 Repl FIP-"',
        'Row("KBO 구원 Repl FIP-"',
        'Row("KBO RA9 WARIP"',
    ]:
        require(constants_source, item, failures, "league constants")

    # Independent arithmetic checks mirroring the documented policy.
    game_count = 720
    target = game_count * 2.0 * (0.500 - 0.294) * 0.43
    if not math.isclose(target, 127.5552, rel_tol=0, abs_tol=1e-9):
        failures.append(f"unexpected 720-game target WAR: {target}")

    pre = 95.0
    innings = 12960.0
    warip = (target - pre) / innings
    post = pre + warip * innings
    if not math.isclose(post, target, rel_tol=0, abs_tol=1e-9):
        failures.append(f"WARIP recentering failed: post={post}, target={target}")

    fwar = 4.0
    ra9war = 5.0
    blend = 0.70 * fwar + 0.30 * ra9war
    if not math.isclose(blend, 4.3, rel_tol=0, abs_tol=1e-9):
        failures.append(f"blend arithmetic failed: {blend}")

    report = {
        "pass": not failures,
        "targetPitcherWarFor720Games": target,
        "sampleWarIp": warip,
        "samplePostCorrectionWar": post,
        "sampleBlendWar": blend,
        "failures": failures,
        "note": "Static/formula validation only; Windows dotnet build remains the final compiler check.",
    }
    output = ROOT / "validation/kbo-pitcher-war-v3-validation.json"
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
