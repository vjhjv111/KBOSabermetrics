from pathlib import Path
root=Path(__file__).resolve().parents[1]
svc=(root/'src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.ParkFactorV2Experiment.cs').read_text(encoding='utf-8')
rows=(root/'src/NaverRelay.Application/Statistics/ParkFactorV2ExperimentRows.cs').read_text(encoding='utf-8')
ui=(root/'src/NaverRelay.Gui/RecordRoomMainForm.cs').read_text(encoding='utf-8')
checks={
 'v2_service':'GetParkFactorV2ExperimentAsync' in svc,
 'five_year_window':'target.Year - 4' in svc,
 'regression':'totalGames / (totalGames + 100.0)' in svc,
 'clamp':'Math.Clamp(regressed, 85.0, 115.0)' in svc,
 'recentering':'100.0 / mean' in svc,
 'ab_metrics':'LegacyPreWar' in rows and 'V2PreWar' in rows and 'V2WarIp' in rows,
 'ui_pf_tab':'KBO PF v2 실험' in ui,
 'ui_ab_tab':'WAR A/B' in ui,
 'legacy_not_replaced':'WAR v3에는 미적용' in ui,
}
failed=[k for k,v in checks.items() if not v]
print({'pass':not failed,'checks':checks,'failures':failed})
raise SystemExit(1 if failed else 0)
