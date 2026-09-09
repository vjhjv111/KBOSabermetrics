#!/usr/bin/env python3
import json
import pathlib
import zipfile
from collections import defaultdict

ROOT = pathlib.Path(__file__).resolve().parents[1]
SAMPLE_ZIP = ROOT / "SampleData" / "2026.zip"
EXTRA_JSON = ROOT / "SampleData" / "20260804HHSS02026.json"

observations = []

def load_game(payload, source):
    result = payload.get("result") or {}
    game = result.get("game") or {}
    relay = result.get("textRelayData") or {}
    for side, lineup_key, team_key in (
        ("home", "homeLineup", "homeTeamCode"),
        ("away", "awayLineup", "awayTeamCode"),
    ):
        team = game.get(team_key)
        lineup = relay.get(lineup_key) or {}
        for role in ("batter", "pitcher"):
            for player in lineup.get(role) or []:
                pcode = str(player.get("pcode") or "").strip()
                name = str(player.get("name") or "").strip()
                if not pcode or not name:
                    continue
                observations.append({
                    "source": source,
                    "gameId": game.get("gameId"),
                    "seasonYear": game.get("seasonYear"),
                    "roundCode": game.get("roundCode"),
                    "team": team,
                    "side": side,
                    "role": role,
                    "pcode": pcode,
                    "name": name,
                    "birth": str(player.get("birth") or "").strip(),
                    "position": str(player.get("posName") or player.get("pos") or "").strip(),
                    "hitType": str(player.get("hitType") or "").strip(),
                    "hasFinalLine": all(key in player for key in ("pcode", "name")) and (
                        role == "batter" or all(key in player for key in ("inn", "run", "er", "bb", "hbp", "kk"))
                    ),
                })

with zipfile.ZipFile(SAMPLE_ZIP) as archive:
    for name in archive.namelist():
        if name.lower().endswith(".json"):
            load_game(json.loads(archive.read(name).decode("utf-8-sig")), name)

if EXTRA_JSON.exists():
    load_game(json.loads(EXTRA_JSON.read_text(encoding="utf-8-sig")), EXTRA_JSON.name)

players = {}
for row in observations:
    player = players.setdefault(row["pcode"], {
        "pcode": row["pcode"],
        "names": set(),
        "birthDates": set(),
        "teams": set(),
        "roles": set(),
        "positions": set(),
        "seasons": set(),
        "games": set(),
    })
    player["names"].add(row["name"])
    if row["birth"]:
        player["birthDates"].add(row["birth"])
    if row["team"]:
        player["teams"].add(row["team"])
    player["roles"].add(row["role"])
    if row["position"]:
        player["positions"].add(row["position"])
    if row["seasonYear"] is not None:
        player["seasons"].add(row["seasonYear"])
    if row["gameId"]:
        player["games"].add(row["gameId"])

name_to_pcodes = defaultdict(set)
for pcode, player in players.items():
    for name in player["names"]:
        name_to_pcodes[name].add(pcode)

duplicate_names = {
    name: sorted(pcodes)
    for name, pcodes in sorted(name_to_pcodes.items())
    if len(pcodes) > 1
}

expected_lee = {
    "51454": {"name": "이승현", "birth": "20020519", "team": "SS", "role": "pitcher"},
    "60146": {"name": "이승현", "birth": "19911120", "team": "SS", "role": "pitcher"},
}
lee_checks = []
for pcode, expected in expected_lee.items():
    player = players.get(pcode)
    lee_checks.append({
        "pcode": pcode,
        "found": player is not None,
        "nameMatches": player is not None and expected["name"] in player["names"],
        "birthMatches": player is not None and expected["birth"] in player["birthDates"],
        "teamMatches": player is not None and expected["team"] in player["teams"],
        "roleMatches": player is not None and expected["role"] in player["roles"],
    })

pitcher_rows = [row for row in observations if row["role"] == "pitcher"]
report = {
    "pass": (
        duplicate_names.get("이승현") == ["51454", "60146"]
        and all(all(value for key, value in check.items() if key != "pcode") for check in lee_checks)
        and all(row["hasFinalLine"] for row in pitcher_rows)
    ),
    "sources": [str(SAMPLE_ZIP), str(EXTRA_JSON)],
    "observationCount": len(observations),
    "uniquePlayerCodeCount": len(players),
    "duplicateNameGroupCount": len(duplicate_names),
    "duplicateNames": duplicate_names,
    "leeSeunghyunChecks": lee_checks,
    "pitcherFinalLineRows": len(pitcher_rows),
    "pitcherFinalLineRowsWithRequiredFields": sum(1 for row in pitcher_rows if row["hasFinalLine"]),
    "notes": [
        "동명이인은 이름이 아닌 pcode로 분리해야 한다.",
        "선수 검색 선택창에는 생년월일·최근 팀·역할을 함께 표시할 수 있다.",
        "투수 경기별 최종 라인에서 IP/R/ER/BB/HBP/SO를 읽을 수 있다.",
    ],
}
output = ROOT / "validation" / "player-profile-data-validation.json"
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
raise SystemExit(0 if report["pass"] else 1)
