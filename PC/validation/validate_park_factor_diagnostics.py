from pathlib import Path
root=Path(__file__).resolve().parents[1]
gui=(root/'src/NaverRelay.Gui/RecordRoomMainForm.cs').read_text(encoding='utf-8')
svc=(root/'src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.ParkFactorDiagnostics.cs').read_text(encoding='utf-8')
dto=(root/'src/NaverRelay.Application/Statistics/ParkFactorDiagnosticRows.cs').read_text(encoding='utf-8')
checks={
 'ui_summary_tab':'"파크팩터 진단"' in gui,
 'ui_detail_tab':'"파크팩터 상세"' in gui,
 'summary_model':'class ParkFactorDiagnosticSeasonRow' in dto,
 'detail_model':'class ParkFactorDiagnosticStadiumRow' in dto,
 'regular_only':"LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'" in svc and "LOWER(TRIM(COALESCE(RoundCode,'')))='kbo_r'" in svc,
 'fip_components':'13.0 * t.HomeRuns' in svc and 't.Strikeouts + t.InfieldFlies' in svc,
 'run_crosscheck':'SimpleRunParkFactor' in svc and 'HomeClubRoadRunsPerGame' in dto,
 'current_pf_compare':'CurrentUsedFipParkFactor' in svc,
 'warnings':'FIP/득점 괴리>=15' in svc,
 'csv_full':'ExportRowsAsync(diagnostics.Stadiums' in gui and 'ExportRowsAsync(diagnostics.Seasons' in gui,
}
print({'pass':all(checks.values()),'checks':checks})
raise SystemExit(0 if all(checks.values()) else 1)
