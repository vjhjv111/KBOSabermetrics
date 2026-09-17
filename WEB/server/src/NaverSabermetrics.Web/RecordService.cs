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
    public const string FormulaVersion = "Uploaded-KboPitcherWarV3-web.8";

    public RecordService(DatabaseCacheService db, SiteOptions options)
    {
        _db = db; _options = options;
        _analytics = new(db); _batters = new(db); _pitchers = new(db);
    }

    public object Schema(string room, string role, string view)
    {
        var definition = ViewRegistry.Get(room == "constants" ? "constants" : role, view);
        return new { columns = Columns(definition), title = definition.Title };
    }

    private IReadOnlyList<WebColumn> Columns(ViewDefinition definition, PropertyInfo? sort = null, bool descending = true)
    {
        var columns = new List<WebColumn>();
        foreach (var p in ViewRegistry.Properties(definition))
        {
            if (PublicHidden(p, definition.Role)) continue;
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
    private static bool PublicHidden(PropertyInfo p, string role)
    {
        if (!string.Equals(role, "pitcher", StringComparison.OrdinalIgnoreCase)) return false;
        var name = p.Name;
        return name.Contains("Ra9War", StringComparison.OrdinalIgnoreCase)
            || name.Contains("BlendWar", StringComparison.OrdinalIgnoreCase);
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
            if (p is null || PublicHidden(p, request.Role) || WarHidden(p) || ContextHidden(p, query, request.Role)) throw new RequestError("현재 탭에서 사용할 수 없는 스탯 조건입니다.");
            _ = ViewRegistry.Threshold(p, condition.Value);
        }
        PropertyInfo? sort = null;
        if (!string.IsNullOrEmpty(request.SortBy))
        {
            sort = properties.FirstOrDefault(x => x.Name == request.SortBy);
            if (sort is null || PublicHidden(sort, request.Role) || WarHidden(sort) || ContextHidden(sort, query, request.Role)) throw new RequestError("허용되지 않은 정렬 열입니다.");
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
                if (PublicHidden(p, request.Role)) continue;
                cells[p.Name] = WarHidden(p) || ContextHidden(p,query,request.Role) ? "-" :
                    p.Name == "Rank" ? (skip + i + 1).ToString(CultureInfo.InvariantCulture) : ViewRegistry.Display(p, p.GetValue(row));
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
            ? "웹 공개 지표는 KBO fWAR만 제공합니다. 사이트 자체 추정치이며 공식 FanGraphs fWAR와 동일한 값은 아닙니다."
            : "업로드된 KBO WAR 계산 소스를 사용합니다. 사이트 자체 추정치입니다.");
        warnings.Add("리그 비교값은 화면 필터와 무관하게 적재된 전체 kbo_r 경기 기준입니다.");
        if (request.Role == "batter" && request.View == "team-batting")
            warnings.Add(request.Room == "team"
                ? "병살 상황은 2아웃 미만에 1루 주자가 있고 타석 결과 전까지 1루를 떠나지 않은 타석입니다. 팀 잔루는 PA-득점-아웃, 희생번트 실패는 번트 타구·번트 파울 후 아웃·번트 헛스윙이 확인된 타석 중 타석 시작 시 가장 앞선 주자가 다음 베이스나 홈까지 진루하지 못한 경우입니다. 번트 파울 뒤 일반 타격 아웃도 포함하며, 안타·볼넷·몸에 맞는 볼 출루는 제외하고 실책에 의한 진루는 실패로 계산합니다."
                : "병살 상황은 2아웃 미만에 1루 주자가 있고 타석 결과 전까지 1루를 떠나지 않은 타석입니다. 선수 잔루는 타자가 아웃된 플레이 후 남은 주자 수, 희생번트 실패는 번트 타구·번트 파울 후 아웃·번트 헛스윙이 확인된 타석 중 타석 시작 시 가장 앞선 주자가 다음 베이스나 홈까지 진루하지 못한 경우입니다. 번트 파울 뒤 일반 타격 아웃도 포함하며, 안타·볼넷·몸에 맞는 볼 출루는 제외하고 실책에 의한 진루는 실패로 계산합니다.");
        if (query.HasSituationFilters) warnings.Add("상황별 재집계: 타격은 타석 시작 상태, 카운트는 도달 타석 기준입니다. 점수는 공격팀 관점이며 ER·공식 IP·WAR·득점/주루 일부는 표시하지 않습니다.");
        if (total > accessible) warnings.Add($"대량 수집 제한으로 정렬 결과 상위 {accessible}행까지만 열람할 수 있습니다.");
        if (_options.Demo) warnings.Insert(0,"샘플 DB입니다. 전체 시즌 기록이 아닙니다.");
        var leagueOverview = request.Room == "team"
            ? await BuildLeagueOverviewAsync(request, query, token).ConfigureAwait(false)
            : null;
        return new(Columns(definition, sort, request.Descending),display,total,accessible,request.Page,request.PageSize,applied,warnings,watch.ElapsedMilliseconds,hit,FormulaVersion,leagueOverview);
    }

    private async Task<LeagueOverview> BuildLeagueOverviewAsync(RecordRequest request, GameQuery query, CancellationToken token)
    {
        var league = await _db.GetLeagueReferenceAsync(cancellationToken: token).ConfigureAwait(false);
        var snapshot = await _analytics.GetWebRoleSnapshotAsync(query, league, request.Role == "pitcher", token).ConfigureAwait(false);
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
            return r.View == "parks" ? lg.ParkFactors.Cast<object>().ToList() : lg.Constants.Cast<object>().ToList();
        }
        var core = r.Role == "batter"
            ? new[] { "basic","advanced","value","extended","power","team-batting","steal","baserunning","discipline" }.Contains(r.View)
            : new[] { "basic","advanced","value","starter","reliever" }.Contains(r.View);
        var needsSnapshot = core || r.QualificationPercent > 0 || !string.IsNullOrEmpty(r.Position);
        var needsLeague = needsSnapshot || r.View is "clutch" or "wp" or "reliever";
        var league = needsLeague ? await _db.GetLeagueReferenceAsync(cancellationToken:token).ConfigureAwait(false) : new LeagueReference();
        var snapshot = needsSnapshot
            ? await _analytics.GetWebRoleSnapshotAsync(q,league,r.Role=="pitcher",token).ConfigureAwait(false)
            : new AnalyticsSnapshot();
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
                case "basic": result=PitcherRecordRoomRowFactory.BuildBasic(snapshot); break;
                case "advanced": result=PitcherRecordRoomRowFactory.BuildAdvanced(snapshot); break;
                case "value": result=PitcherRecordRoomRowFactory.BuildValue(snapshot,league); break;
                case "extended": result=await _pitchers.GetExtendedAsync(q,token).ConfigureAwait(false); break;
                case "wp": result=await _pitchers.GetWinProbabilityAsync(q,league,token).ConfigureAwait(false); break;
                case "runner": result=await _pitchers.GetRunnerAsync(q,token).ConfigureAwait(false); break;
                case "starter":
                {
                    var starts=(await _pitchers.GetStarterAsync(q,token).ConfigureAwait(false)).ToList();
                    var values=PitcherRecordRoomRowFactory.BuildValue(snapshot,league).ToDictionary(v=>PitcherRecordRoomRowFactory.Key(v.Pcode,v.TeamCode));
                    foreach(var x in starts) if(values.TryGetValue(PitcherRecordRoomRowFactory.Key(x.Pcode,x.TeamCode),out var v)) x.StarterWar=v.War;
                    result=starts; break;
                }
                case "reliever":
                {
                    var relievers=(await _pitchers.GetRelieverAsync(q,league,token).ConfigureAwait(false)).ToList();
                    var values=PitcherRecordRoomRowFactory.BuildValue(snapshot,league).ToDictionary(v=>PitcherRecordRoomRowFactory.Key(v.Pcode,v.TeamCode));
                    foreach(var x in relievers) if(values.TryGetValue(PitcherRecordRoomRowFactory.Key(x.Pcode,x.TeamCode),out var v)) x.ReliefWar=v.War;
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

    private static bool ContextHidden(PropertyInfo p, GameQuery q, string role)
    {
        if(!q.HasSituationFilters) return false;
        var n=p.Name;
        if(n.Contains("War",StringComparison.OrdinalIgnoreCase)) return true;
        if(role=="pitcher") return n is "InningsPitched" or "EarnedRuns" or "ERA" or "RA9" or "WHIP" or "CompleteGames" or "Shutouts" or "Fip" or "Xfip" or "FipMinus" or "XfipMinus" or "EraMinusFip" or "LobRate" or "RunsAllowed" or "WildPitches" or "StrikeoutsPerNine" or "WalksPerNine" or "HomeRunsPerNine" or "PitchesPerInning";
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
