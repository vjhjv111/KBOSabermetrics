#!/usr/bin/env python3
"""Static architecture checks for SQLite lazy-loading/memory-bounded GUI changes."""
from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def text(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


checks: list[dict[str, object]] = []


def require(name: str, condition: bool, detail: str) -> None:
    checks.append({"name": name, "pass": bool(condition), "detail": detail})


main = text("src/NaverRelay.Gui/MainForm.cs")
db = text("src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.cs")
analytics = text("src/NaverRelay.Infrastructure.Sqlite/DatabaseAnalyticsService.cs")
workflow = text("src/NaverRelay.Gui/Services/ParsingWorkflowService.cs")
queries = text("src/NaverRelay.Application/Queries/DatabaseQueryModels.cs")
player_db = text("src/NaverRelay.Infrastructure.Sqlite/DatabasePlayerPageService.cs")
all_gui = "\n".join(path.read_text(encoding="utf-8") for path in (ROOT / "src/NaverRelay.Gui").rglob("*.cs"))

require(
    "no-global-all-games-list",
    not re.search(r"\b(?:readonly\s+)?List<NormalizedGame>\s+_games\b", main),
    "MainForm does not retain the whole database as List<NormalizedGame>.",
)
require(
    "no-eager-load-api",
    "LoadAllGamesAsync" not in all_gui,
    "No eager LoadAllGamesAsync path remains in the GUI project.",
)
require(
    "startup-metadata-only",
    "GetCatalogAsync" in main and "GetGameHeadersAsync" in main and "EnsureReadIndexesAsync" in main,
    "Startup uses catalog/header metadata and bounded read-index migration.",
)
require(
    "bounded-index-backfill",
    "BackfillBatchSize = 8" in db and "LIMIT $limit" in db and "IndexVersion" in db,
    "Legacy DB backfill processes at most eight normalized JSON rows per batch and is resumable.",
)
require(
    "sqlite-read-indexes",
    all(token in db for token in ("CREATE TABLE IF NOT EXISTS Players", "CREATE TABLE IF NOT EXISTS GamePlayers", "CREATE TABLE IF NOT EXISTS ComputedCache")),
    "Players, GamePlayers, and ComputedCache read-side tables exist.",
)
require(
    "streaming-analytics",
    "ForEachGameAsync" in analytics and "accumulator.Add(game)" in analytics and "SaveComputedAsync" in analytics,
    "Filtered analytics stream one game at a time and cache only the aggregate snapshot.",
)
require(
    "large-import-does-not-retain-games",
    "documents.Count <= 25" in workflow and "databaseCache.SaveGameAndSourceAsync" in workflow,
    "Only small inputs are retained for validation; large imports persist each game immediately.",
)
require(
    "raw-data-pagination",
    "RawPageSize = 5_000" in main and "GetRawPageDescriptorAsync" in main and "LeadingRowsToSkip" in queries,
    "Raw tabs are limited to 5,000 displayed rows per page.",
)
require(
    "db-player-search",
    "SearchPlayersAsync" in player_db and "LoadPlayerGamesAsync" in player_db,
    "Player search uses the SQLite index and loads only the selected player's games.",
)
require(
    "exact-kbo-r-reference",
    "LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'" in db,
    "Regular-season filtering and league-reference source use exact roundCode=kbo_r.",
)
require(
    "view-cancellation",
    "_viewLoadCts.Cancel()" in main and "화면 조회 취소" in main,
    "Changing filters/tabs or pressing Escape can cancel the current database view load.",
)
require(
    "latest-year-default",
    "selectLatestYear: true" in main and "cboYearFilter.SelectedIndex = 1" in main,
    "The initial view defaults to the latest season rather than all years.",
)
require(
    "no-eager-save-from-global-list",
    "SaveAllAsync(_games" not in all_gui and "_games.AddRange(cachedGames)" not in all_gui,
    "No legacy global-list save/restore pattern remains.",
)

result = {
    "projectRoot": str(ROOT),
    "pass": all(item["pass"] for item in checks),
    "checks": checks,
    "notes": [
        "This is a static architecture check, not a C# compiler or runtime memory benchmark.",
        "Run the Visual Studio build and a representative multi-year DB smoke test on Windows.",
    ],
}
output = ROOT / "validation/lazy-loading-architecture-validation.json"
output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
raise SystemExit(0 if result["pass"] else 1)
