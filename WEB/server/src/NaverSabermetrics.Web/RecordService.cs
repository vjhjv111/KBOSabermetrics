using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NaverRelay.Application.Queries;
using NaverRelay.Application.Statistics;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

public sealed partial class RecordService
{
    private readonly DatabaseCacheService _db;
    private readonly DatabaseAnalyticsService _analytics;
    private readonly DatabaseBatterRecordRoomService _batters;
    private readonly DatabasePitcherRecordRoomService _pitchers;
    private readonly SiteOptions _options;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string,(byte[] Bytes, DateTime At)> _cache = new();
    private long _cacheBytes;
    public const string FormulaVersion = "Uploaded-KboPitcherWarV3-web.12";

    public RecordService(DatabaseCacheService db, SiteOptions options)
    {
        _db = db; _options = options;
        _analytics = new(db); _batters = new(db); _pitchers = new(db);
    }

    public object Schema(string room, string role, string view)
    {
        var definition = ViewRegistry.Get(room == "constants" ? "constants" : role, view);
        return new { columns = Columns(definition, room: room), title = definition.Title };
    }

    private IReadOnlyList<WebColumn> Columns(ViewDefinition definition, PropertyInfo? sort = null, bool descending = true, string room = "")
    {
        var columns = new List<WebColumn>();
        foreach (var p in ViewRegistry.Properties(definition))
        {
            if (PublicHidden(p, definition.Role, room)) continue;
            columns.Add(new(p.Name, ViewRegistry.Label(p), ViewRegistry.Kind(p), ViewRegistry.IsNumber(p) && !WarHidden(p), !WarHidden(p)));
            if (p.Name == "Name")
            {
                var appliedLabel = sort is null
                    ? "적용 조건"
                    : $"적용 조건 = {ViewRegistry.Label(sort)} {(descending ? "↓" : "↑")}";
                columns.Add(new("Applied", appliedLabel, "text", false, false));
            }
        }
        return columns;
    }
    private bool WarHidden(PropertyInfo p) => !_options.ShowWar && p.Name.Contains("War", StringComparison.OrdinalIgnoreCase);

    // Public web policy: keep the calculations internally, but never expose
    // pitcher RA9-WAR or blended WAR in schemas, filters, sorting, or row DTOs.
    // "팬그래프 공식 WAR"가 사이트 대표 투수 WAR이므로, 예전 KBO fWAR("War")과 대체승률.275
    // 비교용 WAR 두 종류도 계산은 그대로 두고 화면에서만 숨깁니다(값 삭제 아님).
    private static bool PublicHidden(PropertyInfo p, string role, string room = "")
    {
        // 팀 단위(AnalyticsGrouping.Team) 집계 행은 한 팀의 여러 선수 수비 이닝을 모두 합친
        // 값이라 "주 포지션"이 개인 의미를 갖지 않으므로 그리드에서 숨깁니다.
        if (string.Equals(room, "team", StringComparison.OrdinalIgnoreCase) && p.Name == "PrimaryPosition") return true;
        if (!string.Equals(role, "pitcher", StringComparison.OrdinalIgnoreCase)) return false;
        var name = p.Name;
        return name.Contains("Ra9War", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BlendWar", StringComparison.OrdinalIgnoreCase)
            || name is "War" or "LoweredReplacementWar" or "LoweredReplacementFanGraphsWar";
    }

    public async Task<TablePage> QueryAsync(RecordRequest request, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        request.Validate(_options);
        var definition = ViewRegistry.Get(request.Room == "constants" ? "constants" : request.Role, request.View);
        var catalog = await _db.GetCatalogAsync(token).ConfigureAwait(false);
        if (request.Year.HasValue && !catalog.Years.Contains(request.Year.Value)) throw new RequestError("DB에 없는 연도입니다.");
        if (!string.IsNullOrEmpty(request.Team) && !catalog.Teams.Contains(request.Team)) throw new RequestError("DB에 없는 팀 코드입니다.");
        if (!string.IsNullOrEmpty(request.Opponent) && !catalog.Teams.Contains(request.Opponent)) throw new RequestError("DB에 없는 상대 팀 코드입니다.");
        if (!string.IsNullOrEmpty(request.Stadium) && !catalog.Stadiums.Contains(request.Stadium)) throw new RequestError("DB에 없는 구장입니다.");
        var query = request.ToQuery(catalog);
        if (request.RecentDays.HasValue)
        {
            var bounds = await _db.GetWebDateBoundsAsync(query with { StartDate=null, EndDate=null, RecentGameCount=null }, token).ConfigureAwait(false);
            if (bounds.Max.HasValue) query = query with { EndDate=bounds.Max, StartDate=bounds.Max.Value.AddDays(1-request.RecentDays.Value) };
        }
        // Do not silently show whole-game ER/WAR on event-only queries.
        if (query.HasSituationFilters && (request.View == "value" || request.Role == "pitcher" && request.View is "starter" or "reliever"))
            throw new RequestError("이 탭은 경기 최종 기록이 필요합니다. 이닝·아웃·주자·카운트 조건을 해제하세요.");
        if (query.HasSituationFilters && request.Role == "batter" && !string.IsNullOrEmpty(request.Position))
            throw new RequestError("상황별 타석에는 정확한 수비 포지션 이닝이 없습니다. 포지션을 전체로 바꾸세요.");
        if (query.HasSituationFilters && request.Role == "pitcher" && request.QualificationPercent > 0)
            throw new RequestError("상황별 조회에서는 공식 투구이닝을 분해할 수 없습니다. 규정이닝을 전체로 바꾸세요.");

        var properties = ViewRegistry.Properties(definition);
        foreach (var condition in request.Conditions)
        {
            var p = properties.FirstOrDefault(x => x.Name == condition.Stat && ViewRegistry.IsNumber(x));
            if (p is null || PublicHidden(p, request.Role, request.Room) || WarHidden(p) || ContextHidden(p, query, request.Role)) throw new RequestError("현재 탭에서 사용할 수 없는 스탯 조건입니다.");
            _ = ViewRegistry.Threshold(p, condition.Value);
        }
        PropertyInfo? sort = null;
        if (!string.IsNullOrEmpty(request.SortBy))
        {
            sort = properties.FirstOrDefault(x => x.Name == request.SortBy);
            if (sort is null || PublicHidden(sort, request.Role, request.Room) || WarHidden(sort) || ContextHidden(sort, query, request.Role)) throw new RequestError("허용되지 않은 정렬 열입니다.");
        }
        var dataVersion = await _db.GetWebSourceVersionAsync(token).ConfigureAwait(false);
        var key = $"web-v3-result-v7|{dataVersion}|{definition.Role}|{definition.Key}|{JsonSerializer.Serialize(query)}|{request.Position}|{request.QualificationPercent.ToString(CultureInfo.InvariantCulture)}";
        var cached = TryRead(key, definition.RowType);
        var hit = cached is not null;
        var rows = cached ?? await ComputeAsync(request, query, definition, token).ConfigureAwait(false);
        if (!hit) Store(key, rows);
        IEnumerable<object> filtered = rows;
        if(request.Room=="team")
        {
            var teamProperty=definition.RowType.GetProperty("TeamCode");
            filtered=filtered.Where(r=>!new[]{"EA","WE"}.Contains(Convert.ToString(teamProperty?.GetValue(r)),StringComparer.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(request.PlayerName))
        {
            var name = definition.RowType.GetProperty("Name");
            if (name is null) throw new RequestError("이 탭은 선수명 검색을 지원하지 않습니다.");
            filtered = filtered.Where(r => Convert.ToString(name.GetValue(r))?.Contains(request.PlayerName.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!string.IsNullOrEmpty(request.PlayerCode))
        {
            var pc = definition.RowType.GetProperty("Pcode");
            if (pc is null) throw new RequestError("선수별 조회를 지원하지 않는 탭입니다.");
            filtered = filtered.Where(r => Convert.ToString(pc.GetValue(r)) == request.PlayerCode);
        }
        Dictionary<string, string>? draftTexts = null;
        if (!string.IsNullOrEmpty(request.Nationality))
        {
            var name = definition.RowType.GetProperty("Name");
            var team = definition.RowType.GetProperty("TeamCode");
            var pc = definition.RowType.GetProperty("Pcode");
            if (name is null) throw new RequestError("이 탭은 국적 조건을 지원하지 않습니다.");
            // 국적 구분은 기본적으로 선수 개인페이지의 "지명순위"(DraftText) 문구를 보고
            // 자동 판별합니다(아시아쿼터/자유선발 포함 여부). PlayerNationalityRegistry의
            // 수동 명단은 그 자동 판별을 예외적으로 덮어쓰는 용도입니다.
            draftTexts = await _db.GetPlayerDraftTextsAsync(token).ConfigureAwait(false);
            filtered = filtered.Where(r =>
            {
                var pcode = pc is null ? null : Convert.ToString(pc.GetValue(r));
                var draftText = !string.IsNullOrEmpty(pcode) ? draftTexts.GetValueOrDefault(pcode) : null;
                var category = PlayerNationalityRegistry.Resolve(
                    Convert.ToString(name.GetValue(r)), team is null ? null : Convert.ToString(team.GetValue(r)), draftText);
                return request.Nationality switch
                {
                    "국내" => category is null,
                    "외국인" => category == PlayerNationalityCategory.Foreign,
                    "아시아쿼터" => category == PlayerNationalityCategory.AsianQuota,
                    "외국인+아쿼" => category is not null,
                    _ => true,
                };
            });
        }
        if (request.RookieEligible)
        {
            var pc = definition.RowType.GetProperty("Pcode");
            if (pc is null) throw new RequestError("이 탭은 신인왕 조건을 지원하지 않습니다.");
            // 신인왕 요건: 해당 시즌 개막 전까지의 정규시즌 통산 누적 기록이
            // 투수는 30이닝, 타자는 60타석 이하인 선수만 남깁니다. KBO 규정상 외국인·
            // 아시아쿼터 선수는 신인왕 후보 자격이 없으므로 함께 제외합니다.
            var limit = request.Role == "batter"
                ? await _db.GetCareerPlateAppearancesBeforeSeasonAsync(request.Year!.Value, token).ConfigureAwait(false)
                : await _db.GetCareerPitchingOutsBeforeSeasonAsync(request.Year!.Value, token).ConfigureAwait(false);
            var threshold = request.Role == "batter" ? 60 : 90; // 30이닝 = 90아웃
            var name = definition.RowType.GetProperty("Name");
            var team = definition.RowType.GetProperty("TeamCode");
            if (name is not null) draftTexts ??= await _db.GetPlayerDraftTextsAsync(token).ConfigureAwait(false);
            filtered = filtered.Where(r =>
            {
                var pcode = Convert.ToString(pc.GetValue(r)) ?? "";
                if (limit.GetValueOrDefault(pcode, 0) > threshold) return false;
                if (name is not null && draftTexts is not null)
                {
                    var draftText = draftTexts.GetValueOrDefault(pcode);
                    var category = PlayerNationalityRegistry.Resolve(
                        Convert.ToString(name.GetValue(r)), team is null ? null : Convert.ToString(team.GetValue(r)), draftText);
                    if (category is not null) return false;
                }
                return true;
            });
        }
        foreach (var condition in request.Conditions)
        {
            var p = properties.Single(x => x.Name == condition.Stat);
            var threshold = ViewRegistry.Threshold(p, condition.Value);
            filtered = filtered.Where(row => Compare(p.GetValue(row), threshold, condition.Operator));
        }
        var sorted = filtered.ToList();
        if (sort is not null)
        {
            var getter = sort;
            sorted.Sort((a,b) => CompareForSort(getter.GetValue(a), getter.GetValue(b), request.Descending));
        }
        var total = sorted.Count;
        var accessible = Math.Min(total, _options.MaxAccessibleRows);
        var applied = Describe(request, query);
        var display = new List<WebRow>();
        var codeProperty = definition.RowType.GetProperty(request.Room=="team"?"TeamCode":"Pcode");
        var skip = (request.Page - 1) * request.PageSize;
        var pageRows = sorted.Take(accessible).Skip(skip).Take(request.PageSize).ToList();
        for (var i = 0; i < pageRows.Count; i++)
        {
            var row = pageRows[i];
            var cells = new Dictionary<string,string>();
            foreach (var p in properties)
            {
                if (PublicHidden(p, request.Role, request.Room)) continue;
                if (WarHidden(p) || ContextHidden(p,query,request.Role)) cells[p.Name] = "-";
                else if (p.Name == "Rank") cells[p.Name] = (skip + i + 1).ToString(CultureInfo.InvariantCulture);
                else
                {
                    var cell = DisplayCell(definition, row, p);
                    cells[p.Name] = query.HasSituationFilters && request.Role == "pitcher" &&
                        cell != "-" && SituationalApproxPitcherStats.Contains(p.Name) ? cell + "*" : cell;
                }
                if (p.Name == "Name")
                {
                    cells["Applied"] = sort is null
                        ? "-"
                        : ViewRegistry.Display(sort, sort.GetValue(row));
                }
            }
            display.Add(new(Convert.ToString(codeProperty?.GetValue(row)), cells));
        }
        var warnings = new List<string>();
        if (!_options.ShowWar) warnings.Add("운영자 설정으로 WAR 표시를 껐습니다.");
        else warnings.Add(request.Role == "pitcher"
            ? "웹 공개 지표는 팬그래프 공식 WAR만 제공합니다(고정 대체수준, FIP 단독). 사이트 자체 추정치이며 공식 FanGraphs fWAR와 동일한 값은 아닙니다."
            : "업로드된 KBO WAR 계산 소스를 사용합니다. 사이트 자체 추정치입니다.");
        warnings.Add("리그 비교값은 화면 필터와 무관하게 적재된 전체 kbo_r 경기 기준입니다.");
        if (request.Role == "batter" && request.View == "team-batting")
            warnings.Add(request.Room == "team"
                ? "병살 상황은 2아웃 미만에 1루 주자가 있고 타석 결과 전까지 1루를 떠나지 않은 타석입니다. 팀 잔루는 PA-득점-아웃입니다. 희생번트 실패는 주자가 있는 2아웃 미만의 번트 관련 타석 중 안타·희생타 성공·실책 출루·볼넷류·야수선택이 아닌 타자 아웃입니다. 번트 아웃은 바로 포함하고, 그 밖의 아웃은 첫 2스트라이크를 번트 파울·번트 헛스윙·루킹 스트라이크로만 만든 타석에 한해 포함합니다."
                : "병살 상황은 2아웃 미만에 1루 주자가 있고 타석 결과 전까지 1루를 떠나지 않은 타석입니다. 선수 잔루는 타자가 아웃된 플레이 후 남은 주자 수입니다. 희생번트 실패는 주자가 있는 2아웃 미만의 번트 관련 타석 중 안타·희생타 성공·실책 출루·볼넷류·야수선택이 아닌 타자 아웃입니다. 번트 아웃은 바로 포함하고, 그 밖의 아웃은 첫 2스트라이크를 번트 파울·번트 헛스윙·루킹 스트라이크로만 만든 타석에 한해 포함합니다.");
        if (query.HasSituationFilters) warnings.Add("상황별 재집계: 타격은 타석 시작 상태, 카운트는 도달 타석 기준입니다. 점수는 공격팀 관점입니다. * 표시된 IP·ERA·RA9·WHIP·FIP·xFIP·K/9·BB/9·HR/9 등 이닝 기반 스탯은 상황 조건에 해당하는 타석만으로 다시 계산한 근사치입니다. 그중 ERA*는 진짜 자책점이 아니라(타석 단위 데이터에는 자책·비자책 구분이 없음) 그 투수의 시즌 전체 자책점/실점 비율을 상황별 실점(RA9*)에 곱한 추정치입니다. WAR·득점/주루 관련 지표는 표시하지 않습니다.");
        if (total > accessible) warnings.Add($"대량 수집 제한으로 정렬 결과 상위 {accessible}행까지만 열람할 수 있습니다.");
        if (_options.Demo) warnings.Insert(0,"샘플 DB입니다. 전체 시즌 기록이 아닙니다.");
        var leagueOverview = request.Room == "team"
            ? await BuildLeagueOverviewAsync(request, query, token).ConfigureAwait(false)
            : null;
        return new(Columns(definition, sort, request.Descending, request.Room),display,total,accessible,request.Page,request.PageSize,applied,warnings,watch.ElapsedMilliseconds,hit,FormulaVersion,leagueOverview);
    }

    private static string DisplayCell(ViewDefinition definition, object row, PropertyInfo property)
    {
        if (definition.RowType == typeof(LeagueConstantGridRow) &&
            property.Name == nameof(LeagueConstantGridRow.Value) &&
            row is LeagueConstantGridRow constant &&
            constant.Value.HasValue)
        {
            var isPlateAppearanceCount = constant.Metric.EndsWith("표본 PA", StringComparison.Ordinal);
            return constant.Value.Value.ToString(
                isPlateAppearanceCount ? "N0" : "0.0000",
                CultureInfo.InvariantCulture);
        }

        return ViewRegistry.Display(property, property.GetValue(row));
    }

    private async Task<LeagueOverview> BuildLeagueOverviewAsync(RecordRequest request, GameQuery query, CancellationToken token)
    {
        var league = await _db.GetLeagueReferenceAsync(cancellationToken: token).ConfigureAwait(false);
        var snapshot = await _analytics.GetWebRoleSnapshotAsync(query, league, request.Role == "pitcher",
            includeTeamBattingContext: false, cancellationToken: token).ConfigureAwait(false);
        var fullLeague = string.IsNullOrWhiteSpace(request.Team);
        LeagueOverview overview;
        if (request.Role == "pitcher")
        {
            // 팀 완봉은 개인 완봉(한 투수가 던진 완봉)의 합이 아니라, 필터링된 경기 중
            // 상대팀을 무실점으로 막은 경기 수입니다(여러 투수가 이어 던진 합작 완봉 포함).
            var teamShutouts = await _db.GetTeamShutoutsAsync(query, token).ConfigureAwait(false);
            var shutouts = fullLeague ? teamShutouts.Values.Sum() : teamShutouts.GetValueOrDefault(request.Team!, 0);
            overview = LeagueOverviewBuilder.FromPitchers(PitcherRecordRoomRowFactory.BuildBasic(snapshot), fullLeague, shutouts);
        }
        else
        {
            overview = LeagueOverviewBuilder.FromBatters(RecordRoomRowFactory.BuildBasic(snapshot), fullLeague);
        }

        if (request.Role == "pitcher")
        {
            overview = overview with
            {
                Metrics = overview.Metrics
                    .Where(m => !m.Label.Contains("RA9-WAR", StringComparison.OrdinalIgnoreCase)
                             && !m.Label.Contains("Blend WAR", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            };
        }
        return overview;
    }

    private async Task<List<object>> ComputeAsync(RecordRequest r, GameQuery q, ViewDefinition def, CancellationToken token)
    {
        if (r.Room == "constants")
        {
            if (r.View == "parks-detail")
            {
                var detail = await _db.GetParkFactorByHitTypeAsync(token).ConfigureAwait(false);
                if (r.Year.HasValue) detail = detail.Where(x => x.Year == r.Year.Value).ToList();
                return detail.Cast<object>().ToList();
            }
            var lg = await _db.GetLeagueReferenceAsync(cancellationToken: token).ConfigureAwait(false);
            if (r.View == "formulas") return BuildFormulaRows(lg, r.Year).Cast<object>().ToList();
            if (r.View == "parks") return lg.ParkFactors.Cast<object>().ToList();
            if (r.Year.HasValue)
            {
                var prefix = $"{r.Year.Value} ";
                return lg.Constants
                    .Where(row => row.Metric.StartsWith(prefix, StringComparison.Ordinal))
                    .Cast<object>()
                    .ToList();
            }
            return lg.Constants
                .Where(row => !HasSeasonPrefix(row.Metric))
                .Cast<object>()
                .ToList();
        }
        var core = r.Role == "batter"
            ? new[] { "basic","advanced","value","extended","power","team-batting","steal","baserunning","discipline" }.Contains(r.View)
            : new[] { "basic","advanced","value","starter","reliever" }.Contains(r.View);
        var needsSnapshot = core || r.QualificationPercent > 0 || !string.IsNullOrEmpty(r.Position);
        var needsLeague = needsSnapshot || r.View is "clutch" or "wp" or "reliever";
        var league = needsLeague ? await _db.GetLeagueReferenceAsync(cancellationToken:token).ConfigureAwait(false) : new LeagueReference();
        var snapshot = needsSnapshot
            ? await _analytics.GetWebRoleSnapshotAsync(q,league,r.Role=="pitcher",
                includeTeamBattingContext: r.Role=="batter" && r.View=="team-batting", cancellationToken:token).ConfigureAwait(false)
            : new AnalyticsSnapshot();
        // ERA*(추정치)는 상황 조건을 뺀 시즌 전체 자책점/실점 비율이 필요합니다. 상황 필터가
        // 걸린 snapshot.PitcherValues는 항상 비어 있으므로(공식 최종 기록은 상황별로 쪼갤 수
        // 없어 FinalGames=0으로 집계됨), 상황 조건만 제거한 별도 쿼리로 따로 가져옵니다.
        IReadOnlyDictionary<string,PitcherValueGridRow>? seasonPitcherValues = null;
        if (r.Role == "pitcher" && q.HasSituationFilters && new[] { "basic","advanced" }.Contains(r.View))
        {
            var seasonQuery = q with
            {
                InningFilter = null, OutsBefore = null, RunnerState = null,
                ScoreSituation = null, BallsBefore = null, StrikesBefore = null, BatOrder = null,
            };
            var seasonSnapshot = await _analytics.GetWebRoleSnapshotAsync(seasonQuery, league, true, cancellationToken: token).ConfigureAwait(false);
            seasonPitcherValues = seasonSnapshot.PitcherValues.ToDictionary(
                v => PitcherRecordRoomRowFactory.Key(v.Pcode, v.TeamCode), StringComparer.Ordinal);
        }
        object result;
        if (r.Role == "batter")
        {
            result = r.View switch
            {
                "basic" => (object)RecordRoomRowFactory.BuildBasic(snapshot),
                "advanced" => RecordRoomRowFactory.BuildAdvanced(snapshot),
                "value" => RecordRoomRowFactory.BuildValue(snapshot),
                "extended" => RecordRoomRowFactory.BuildExtended(snapshot),
                "power" => RecordRoomRowFactory.BuildPower(snapshot),
                "team-batting" => RecordRoomRowFactory.BuildTeamBatting(snapshot),
                "steal" => RecordRoomRowFactory.BuildSteal(snapshot),
                "baserunning" => RecordRoomRowFactory.BuildBaserunning(snapshot),
                "discipline" => RecordRoomRowFactory.BuildPitchProfile(snapshot),
                "clutch" => await _batters.GetClutchAsync(q,league,token).ConfigureAwait(false),
                "batted-ball" => await _batters.GetBattedBallAsync(q,token).ConfigureAwait(false),
                "direction" => await _batters.GetDirectionAsync(q,token).ConfigureAwait(false),
                "pitch-types" => BatterPitchTypeMatrixFactory.Build(await _batters.GetPitchTypesAsync(q,token).ConfigureAwait(false)),
                _ => throw new RequestError("알 수 없는 타자 탭입니다.")
            };
        }
        else
        {
            switch(r.View)
            {
                case "basic": result=PitcherRecordRoomRowFactory.BuildBasic(snapshot,q.HasSituationFilters,seasonPitcherValues); break;
                case "advanced": result=PitcherRecordRoomRowFactory.BuildAdvanced(snapshot,q.HasSituationFilters,seasonPitcherValues); break;
                case "value": result=PitcherRecordRoomRowFactory.BuildValue(snapshot,league); break;
                case "extended": result=await _pitchers.GetExtendedAsync(q,token).ConfigureAwait(false); break;
                case "wp": result=await _pitchers.GetWinProbabilityAsync(q,league,token).ConfigureAwait(false); break;
                case "runner": result=await _pitchers.GetRunnerAsync(q,token).ConfigureAwait(false); break;
                case "starter":
                {
                    var starts=(await _pitchers.GetStarterAsync(q,token).ConfigureAwait(false)).ToList();
                    var values=PitcherRecordRoomRowFactory.BuildValue(snapshot,league).ToDictionary(v=>PitcherRecordRoomRowFactory.Key(v.Pcode,v.TeamCode));
                    foreach(var x in starts) if(values.TryGetValue(PitcherRecordRoomRowFactory.Key(x.Pcode,x.TeamCode),out var v)) x.StarterWar=v.FanGraphsWar;
                    result=starts; break;
                }
                case "reliever":
                {
                    var relievers=(await _pitchers.GetRelieverAsync(q,league,token).ConfigureAwait(false)).ToList();
                    var values=PitcherRecordRoomRowFactory.BuildValue(snapshot,league).ToDictionary(v=>PitcherRecordRoomRowFactory.Key(v.Pcode,v.TeamCode));
                    foreach(var x in relievers) if(values.TryGetValue(PitcherRecordRoomRowFactory.Key(x.Pcode,x.TeamCode),out var v)) x.ReliefWar=v.FanGraphsWar;
                    result=relievers; break;
                }
                case "batted-ball": result=await _pitchers.GetBattedBallAsync(q,token).ConfigureAwait(false); break;
                case "direction": result=await _pitchers.GetDirectionAsync(q,token).ConfigureAwait(false); break;
                case "discipline": result=await _pitchers.GetPitchProfileAsync(q,token).ConfigureAwait(false); break;
                case "pitch-types": result=await _pitchers.GetPitchTypesAsync(q,token).ConfigureAwait(false); break;
                default: throw new RequestError("알 수 없는 투수 탭입니다.");
            }
        }
        var rows=((IEnumerable)result).Cast<object>().ToList();
        if (rows.Count > 50000) throw new RequestError("집계 결과가 너무 큽니다. 연도/팀을 지정하세요.", 422, "RESULT_TOO_LARGE");
        if (r.Room != "team" && (r.QualificationPercent>0 || !string.IsNullOrEmpty(r.Position)))
        {
            var eligible=new HashSet<string>(StringComparer.Ordinal);
            if(r.Role=="batter")
            {
                foreach(var x in snapshot.BatterClassic)
                {
                    var key=RecordRoomRowFactory.Key(x.Pcode,x.TeamCode);
                    if(!string.IsNullOrEmpty(r.Position) && snapshot.PrimaryPositions.GetValueOrDefault(key,"-") != r.Position) continue;
                    var teamGames=snapshot.TeamGames.GetValueOrDefault(x.TeamCode??"",x.Games);
                    if(x.PA+1e-7 >= teamGames*3.1*r.QualificationPercent/100) eligible.Add(key);
                }
            }
            else foreach(var x in snapshot.PitcherClassic)
            {
                var key=RecordRoomRowFactory.Key(x.Pcode,x.TeamCode);
                var teamGames=snapshot.TeamGames.GetValueOrDefault(x.TeamCode??"",x.Games);
                if(snapshot.PitcherIp.GetValueOrDefault(key,0)+1e-7 >= teamGames*r.QualificationPercent/100) eligible.Add(key);
            }
            var p=def.RowType.GetProperty("Pcode"); var team=def.RowType.GetProperty("TeamCode");
            rows=rows.Where(x=>eligible.Contains(RecordRoomRowFactory.Key(Convert.ToString(p?.GetValue(x)),Convert.ToString(team?.GetValue(x))))).ToList();
        }
        return rows;
    }

    private static bool HasSeasonPrefix(string metric) =>
        metric.Length > 4 && metric[4] == ' ' && int.TryParse(metric.AsSpan(0, 4), out _);

    private static IReadOnlyList<SabermetricFormulaGridRow> BuildFormulaRows(LeagueReference league, int? seasonYear)
    {
        var woba = league.GetWobaConstants(seasonYear);
        var wobaScope = woba.SeasonYear?.ToString(CultureInfo.InvariantCulture) ?? "통합";
        var wobaWeights = $"{wobaScope} {woba.Source}; PA {woba.SamplePlateAppearances:N0}; " +
                          $"uBB {woba.UnintentionalWalk:0.0000}, HBP {woba.HitByPitch:0.0000}, " +
                          $"1B {woba.Single:0.0000}, 2B {woba.Double:0.0000}, " +
                          $"3B {woba.Triple:0.0000}, HR {woba.HomeRun:0.0000}";
        var calibration = league.PitcherWar ?? new PitcherWarCalibration();

        static SabermetricFormulaGridRow Row(
            string category,
            string metric,
            string formula,
            string constants,
            string description) => new()
            {
                Category = category,
                Metric = metric,
                Formula = formula,
                Constants = constants,
                Description = description,
            };

        return
        [
            Row("기본 타격", "AVG", "H ÷ AB", "-", "타수 대비 안타 비율"),
            Row("기본 타격", "OBP", "(H + BB + HBP) ÷ (AB + BB + HBP + SF)", "-", "출루율"),
            Row("기본 타격", "SLG", "(1B + 2×2B + 3×3B + 4×HR) ÷ AB", "-", "장타율"),
            Row("기본 타격", "OPS", "OBP + SLG", "-", "출루율과 장타율의 합"),
            Row("기본 타격", "ISO", "SLG - AVG", "-", "순수 장타력"),
            Row("기본 타격", "BABIP", "(H - HR) ÷ (AB - SO - HR + SF)", "-", "인플레이 타구의 안타 비율"),
            Row("기본 타격", "BB% · K%", "BB ÷ PA · SO ÷ PA", "-", "타석당 볼넷과 삼진"),
            Row("기본 타격", "BB/K", "BB ÷ SO", "-", "삼진 대비 볼넷"),

            Row("타격 가치", "wOBA", "(wBB×uBB + wHBP×HBP + w1B×1B + w2B×2B + w3B×3B + wHR×HR) ÷ (AB + uBB + HBP + SF)", wobaWeights, "시즌 RE24 이벤트 득점가치를 리그 OBP에 맞춰 스케일링"),
            Row("타격 가치", "wRAA", "((wOBA - lgwOBA) ÷ Scale) × PA", $"lgwOBA {woba.LeagueWoba:0.0000}; Scale {woba.Scale:0.0000}", "평균 타자 대비 득점 기여"),
            Row("타격 가치", "wRC", "wRAA + (lgR/PA × PA)", $"lgR/PA {woba.RunsPerPa:0.0000}", "선수가 창출한 추정 득점"),
            Row("타격 가치", "wRC+", "100 × (wRC ÷ PA) ÷ lgR/PA", $"lgR/PA {woba.RunsPerPa:0.0000}", "100이 리그 평균"),
            Row("타격 가치", "wRC+(파크)", "wRC+ + (100 - 타자 PF)", "KBO PF v2; 선수의 구장별 PA 가중", "구장 효과를 단순 점수 보정한 사이트 지표"),
            Row("타격 가치", "OPS+", "100 × (OBP÷lgOBP + SLG÷lgSLG - 1)", $"통합 lgOBP {league.Obp:0.0000}; lgSLG {league.Slg:0.0000}", "100이 리그 평균"),

            Row("타자 WAR", "주루 Runs", "0.20×SB - 0.40×CS", "SB +0.20; CS -0.40 runs", "현재 도루·도실패만 반영"),
            Row("타자 WAR", "포지션 Runs", "FG 포지션 rate × 추정 수비이닝 ÷ 1458", "DH는 PA÷600; 포지션별 FG rate", "수비 출전 정보로 주 포지션과 보정치 추정"),
            Row("타자 WAR", "RAR", "wRAA + 주루 Runs + 포지션 Runs + 대체선수 Runs", "수비 Runs 0; 대체선수 Runs는 조회 범위 목표 WAR에 맞춰 역산", "대체선수 대비 득점"),
            Row("타자 WAR", "Site WAR", "RAR ÷ Runs Per Win", "Runs Per Win 10.0; 타자 목표 WAR 57%", "사이트 자체 추정 타자 WAR"),

            Row("기본 투구", "ERA", "9 × ER ÷ IP", "공식 ER·IP", "9이닝당 자책점"),
            Row("기본 투구", "RA9", "9 × R ÷ IP", "공식 R·IP", "9이닝당 실점"),
            Row("기본 투구", "WHIP", "(H + BB) ÷ IP", "공식 투수 최종 기록", "이닝당 출루 허용"),
            Row("기본 투구", "K-BB%", "(SO - BB) ÷ TBF", "-", "상대한 타자 대비 삼진과 볼넷 차이"),
            Row("기본 투구", "K/9 · BB/9 · HR/9", "9 × SO·BB·HR ÷ IP", "공식 투수 최종 기록", "9이닝 기준 비율"),
            Row("기본 투구", "투수 BABIP", "(H - HR) ÷ (TBF - BB - HBP - SO - HR + SF)", "-", "피인플레이 타구의 안타 비율"),
            Row("기본 투구", "LOB%", "(H + BB + HBP - R) ÷ (H + BB + HBP - 1.4×HR)", "HR 계수 1.4", "주자 잔류율 추정"),

            Row("수비 독립 투구", "FIP", "[13×HR + 3×(BB+HBP) - 2×SO] ÷ IP + C", $"C {league.FipConstant:0.0000}", "리그 평균 FIP가 리그 RA9에 맞도록 C 산출"),
            Row("수비 독립 투구", "xFIP", "[13×(FB×lgHR/FB) + 3×(BB+HBP) - 2×SO] ÷ IP + C", $"lgHR/FB {league.HrPerFlyBall:0.0000}; C {league.FipConstant:0.0000}", "실제 홈런을 기대 홈런으로 대체"),
            Row("수비 독립 투구", "FIP- · xFIP-", "100 × 선수 FIP 또는 xFIP ÷ 리그 RA9", $"리그 RA9 {league.Ra9:0.0000}", "100보다 낮을수록 우수"),

            Row("KBO 투수 WAR", "ifFIP", "[13×HR + 3×(BB+HBP) - 2×(SO+IFFB)] ÷ IP + C", $"C {league.IfFipConstant:0.0000}", "내야 뜬공을 삼진과 같은 자동 아웃으로 추가"),
            Row("KBO 투수 WAR", "FIPR9", "ifFIP + (lgRA9 - lgERA)", $"보정 {league.Ra9Adjustment:0.0000}; lgFIPR9 {league.LeagueFipR9:0.0000}", "ifFIP를 실점 스케일로 변환"),
            Row("KBO 투수 WAR", "pFIPR9", "FIPR9 ÷ (PF÷100)", "시즌·구장별 KBO PF v2", "투구이닝으로 구장 팩터를 가중"),
            Row("KBO 투수 WAR", "dRPW", "{[(18-IP/G)×lgFIPR9 + (IP/G)×pFIPR9]÷18 + 2}×1.5", $"lgFIPR9 {league.LeagueFipR9:0.0000}", "투수 역할과 실점 환경별 동적 승리당 득점"),
            Row("KBO 투수 WAR", "gmLI", "구원 등판 시 평균 |WPA| ÷ 리그 평균 |WPA|", $"리그 평균 |WPA| {league.AverageAbsoluteWpa:0.0000}; 0.1~5.0 제한", "구원 등판 시점의 평균 레버리지"),
            Row("KBO 투수 WAR", "구원 LI 배수", "(1 + gmLI) ÷ 2", "선발 1.0", "구원 품질·대체승에 적용"),
            Row("KBO 투수 WAR", "평균 대비 승리 기여", "(lgFIPR9 - pFIPR9) ÷ dRPW × IP÷9 × LI", "구원만 LI 적용", "리그 평균보다 억제한 실점을 승리 단위로 환산"),
            Row("KBO 투수 WAR", "대체 승", "(Repl FIPR9 - lgFIPR9) ÷ dRPW × IP÷9 × LI", $"SP {calibration.StarterReplacementFipR9:0.0000}; RP {calibration.RelieverReplacementFipR9:0.0000}", "선발·구원 역할별 대체수준"),
            Row("KBO 투수 WAR", "KBO fWAR", "평균 대비 승리 기여 + 대체 승 + WARIP×IP", $"WARIP {calibration.FipWarPerInning:0.000000}; 투수 WAR 몫 {calibration.PitcherWarShare:P0}; 대체승률 {calibration.ReplacementWinningPercentage:0.000}", "비공개(참고용) - 선발·구원 역할별 관측 대체수준을 쓰는 이전 버전 공식"),

            Row("팬그래프 공식 투수 WAR", "ifFIP · FIPR9 · pFIPR9 · dRPW · gmLI · 구원 LI 배수", "KBO 투수 WAR과 동일", "-", "이 구간까지는 KBO 투수 WAR과 계산식이 같습니다"),
            Row("팬그래프 공식 투수 WAR", "평균 대비 승리 기여", "(lgFIPR9 - pFIPR9) ÷ dRPW × IP÷9 × LI", "구원만 LI 적용", "리그 평균보다 억제한 실점을 승리 단위로 환산 (KBO 투수 WAR과 동일)"),
            Row("팬그래프 공식 투수 WAR", "대체 승", "대체수준(승/경기) × IP÷9", "구원 0.03승/경기; 선발 0.12승/경기; 그 투수 시즌 GS/G로 가중평균", "선발·구원 역할별 관측치 대신 팬그래프 원 공식의 고정 대체수준을 사용 (LI 미적용)"),
            Row("팬그래프 공식 투수 WAR", "팬그래프 공식 WAR", "평균 대비 승리 기여 + 대체 승 + WARIP×IP", $"WARIP {calibration.FanGraphsWarPerInning:0.000000}(다시즌 참고값; 실제 표시값은 조회 범위별 재계산); 투수 WAR 몫 {calibration.PitcherWarShare:P0}; 대체승률 {calibration.ReplacementWinningPercentage:0.000}", "사이트 대표 투수 WAR(기본/가치 탭의 \"팬그래프 공식 WAR\" 컬럼) - RA9 블렌드 없이 FIP 단독"),

            Row("투구 접근", "Swing%", "Swing ÷ Pitches", "-", "전체 투구 중 스윙 비율"),
            Row("투구 접근", "Contact%", "Contact ÷ Swing", "-", "스윙 중 컨택 비율"),
            Row("투구 접근", "Whiff%", "Whiff ÷ Swing", "-", "스윙 중 헛스윙 비율"),
            Row("투구 접근", "CSW%", "(Whiff + Called Strike) ÷ Pitches", "-", "헛스윙과 루킹 스트라이크 비율"),
            Row("투구 접근", "Z-Swing% · O-Swing%", "Zone Swing÷Zone Pitches · Chase Swing÷Out-Zone Pitches", "-", "존 안 스윙과 존 밖 추격 비율"),
            Row("투구 접근", "Z-Contact% · O-Contact%", "Zone Contact÷Zone Swing · Out-Zone Contact÷Chase Swing", "-", "존 안팎 컨택 비율"),
            Row("투구 접근", "SwStr% · 1st Swing%", "Whiff÷Pitches · First-Pitch Swing÷First Pitches", "-", "전체 헛스윙과 초구 스윙 비율"),
            Row("투구 접근", "P/PA", "Pitches ÷ PA 또는 TBF", "-", "타자·투수의 타석당 투구 수"),

            Row("상황 가치", "RE24", "타석 후 기대득점 - 타석 전 기대득점 + 실제 득점", "24개 주자·아웃 상태", "wOBA 이벤트 가중치의 기초"),
            Row("상황 가치", "WPA", "타석 후 승리확률 - 타석 전 승리확률", "원본 metricOption.wpaByPlate", "한 타석이 승리확률에 준 변화"),
            Row("상황 가치", "pLI", "타석의 |WPA| ÷ 리그 평균 |WPA|", $"리그 평균 |WPA| {league.AverageAbsoluteWpa:0.0000}", "상황 중요도"),
            Row("상황 가치", "WPA/LI", "WPA ÷ pLI", "-", "상황 중요도를 중립화한 WPA"),

            Row("구장", "KBO PF v2", "최근 5년 PF 가중결합 → 100 회귀 → 85~115 제한 → 리그 평균 100 재중앙화", "최근가중 30/25/20/15/10%; Reliability=G÷(G+100)", "타자는 구장별 PA, 투수는 구장별 IP로 가중"),
        ];
    }

    // 상황 조건(이닝·아웃·주자·카운트) 필터가 걸리면 공식 최종 기록 단위로만 정확한 항목은
    // 계속 숨깁니다. IP·ERA·RA9·WHIP·FIP·xFIP·K9·BB9·HR9 등은 타석 단위 근사치(*)로
    // PitcherRecordRoomRowFactory에서 다시 계산되므로 더 이상 숨기지 않습니다.
    internal static readonly HashSet<string> SituationalApproxPitcherStats = new(StringComparer.Ordinal)
    {
        "InningsPitched","ERA","RA9","WHIP","Fip","Xfip","FipMinus","XfipMinus","EraMinusFip",
        "LobRate","RunsAllowed","StrikeoutsPerNine","WalksPerNine","HomeRunsPerNine","PitchesPerInning",
    };
    private static bool ContextHidden(PropertyInfo p, GameQuery q, string role)
    {
        if(!q.HasSituationFilters) return false;
        var n=p.Name;
        if(n.Contains("War",StringComparison.OrdinalIgnoreCase)) return true;
        if(role=="pitcher") return n is "EarnedRuns" or "CompleteGames" or "Shutouts" or "WildPitches";
        return n is "PrimaryPosition" or "Runs" or "RunsBattedIn" or "StolenBases" or "CaughtStealing" or "RunningRuns" or "PositionRuns" or "RunsAboveReplacement" or "DoublePlays";
    }
    private static bool Compare(object? x, double y, string op)
    {
        if(x is null) return false;
        var d=Convert.ToDouble(x,CultureInfo.InvariantCulture);
        if(!double.IsFinite(d)) return false;
        return op switch { "gte"=>d>=y,"gt"=>d>y,"lte"=>d<=y,"lt"=>d<y,"eq"=>Math.Abs(d-y)<1e-9,_=>false };
    }
    private static int CompareForSort(object? a,object? b,bool desc)
    {
        if(a is null) return b is null ? 0 : 1;
        if(b is null) return -1;
        int c = a is string sa && b is string sb ? StringComparer.OrdinalIgnoreCase.Compare(sa,sb) : ((IComparable)a).CompareTo(b);
        return desc ? -c:c;
    }
    private List<object>? TryRead(string key,Type rowType)
    {
        byte[]? bytes=null;
        lock(_cacheLock) if(_cache.TryGetValue(key,out var item) && item.At>DateTime.UtcNow.AddMinutes(-20)) bytes=item.Bytes;
        return bytes is null ? null : ((IEnumerable)JsonSerializer.Deserialize(bytes,typeof(List<>).MakeGenericType(rowType))!).Cast<object>().ToList();
    }
    private void Store(string key,List<object> rows)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(rows);
        if(bytes.Length>8*1024*1024)return;
        lock(_cacheLock)
        {
            if(_cache.Remove(key,out var old))_cacheBytes-=old.Bytes.Length;
            while(_cache.Count>=64 || _cacheBytes+bytes.Length>32*1024*1024)
            {
                var k=_cache.MinBy(x=>x.Value.At).Key;
                _cacheBytes-=_cache[k].Bytes.Length;_cache.Remove(k);
            }
            _cache[key]=(bytes,DateTime.UtcNow);_cacheBytes+=bytes.Length;
        }
    }
    private static string Describe(RecordRequest r,GameQuery q)
    {
        var parts=new List<string> { r.Room=="career" ? "통산" : r.Year?.ToString()??"전체 연도", r.Competition };
        if(!string.IsNullOrEmpty(r.Team))parts.Add("팀 "+r.Team);
        if(!string.IsNullOrEmpty(r.Position))parts.Add(r.Position);
        if(!string.IsNullOrEmpty(r.Nationality))parts.Add(r.Nationality);
        if(r.RookieEligible)parts.Add("신인왕 요건");
        if(r.QualificationPercent>0)parts.Add($"규정 {r.QualificationPercent:0.#}%");
        if(q.StartDate.HasValue || q.EndDate.HasValue) parts.Add($"{q.StartDate:yyyy-MM-dd}~{q.EndDate:yyyy-MM-dd}");
        if(r.RecentGames.HasValue)parts.Add($"최근 {r.RecentGames}경기");
        foreach(var s in new[]{r.Venue,r.Weekday,r.Stadium,r.Inning,r.Runners,r.Score})if(!string.IsNullOrEmpty(s))parts.Add(s);
        if(!string.IsNullOrEmpty(r.Opponent))parts.Add("vs "+r.Opponent);
        if(r.Outs.HasValue)parts.Add($"{r.Outs}아웃");
        if(r.Balls.HasValue)parts.Add($"{r.Balls}-{r.Strikes} 도달");
        if(r.BatOrder.HasValue)parts.Add($"{r.BatOrder}번");
        foreach(var c in r.Conditions)parts.Add($"{c.Stat} {c.Operator} {c.Value.ToString(CultureInfo.InvariantCulture)}");
        return string.Join(" · ",parts);
    }
}
