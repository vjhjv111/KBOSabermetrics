#!/usr/bin/env python3
from pathlib import Path
import json, sys
root=Path(__file__).resolve().parents[1]
ui=(root/'src/NaverRelay.Gui/RecordRoomMainForm.cs').read_text(encoding='utf-8')
svc=(root/'src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.PitcherWarDiagnostics.cs').read_text(encoding='utf-8')
model=(root/'src/NaverRelay.Application/Statistics/PitcherWarDiagnosticRows.cs').read_text(encoding='utf-8')
checks={
 'ui_tabs': all(x in ui for x in ['투수 WAR 진단','대체후보','GetWarDiagnosticsAsync']),
 'season_model': 'PitcherWarDiagnosticSeasonRow' in model,
 'candidate_model': 'PitcherReplacementCandidateRow' in model,
 'percentiles': all(x in svc for x in ['0.25','0.50','0.75','0.90','RollingPercentile']),
 'annual_regular_season_sql': "LOWER(TRIM(COALESCE(g.RoundCode,'')))='kbo_r'" in svc and 'GROUP BY COALESCE(g.SeasonYear,0)' in svc,
 'candidate_identity': all(x in model for x in ['Pcode','Name','TeamCodes','ObservedFipMinus','RegressedFipMinus']),
 'war_diagnostic': all(x in model for x in ['TargetPitcherWar','PreCorrectionFipWar','FipWarPerInning','PreCorrectionRa9War','Ra9WarPerInning']),
}
result={'pass':all(checks.values()),'checks':checks,'note':'Static diagnostic feature validation; Windows dotnet build remains final compiler check.'}
print(json.dumps(result,ensure_ascii=False,indent=2))
sys.exit(0 if result['pass'] else 1)
