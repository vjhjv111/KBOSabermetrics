using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>Small season snapshots built from the same read-only warehouse as the record room.</summary>
public sealed partial class DiamondRosterService
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, CacheEntry> _cache = [];
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);
    private sealed record CacheEntry(string Stamp, DateTime At, DiamondRoster Roster);
    private const string RegularGames = "g.SeasonYear=$year AND g.RoundCode='kbo_r' AND g.StatusCode='RESULT' AND g.AwayTeamCode NOT IN ('EA','WE') AND g.HomeTeamCode NOT IN ('EA','WE')";
    private static readonly Dictionary<string, string> TeamNames = new(StringComparer.Ordinal)
    { ["HH"]="한화",["HT"]="KIA",["KT"]="KT",["LG"]="LG",["LT"]="롯데",["NC"]="NC",["OB"]="두산",["SK"]="SSG",["SS"]="삼성",["WO"]="키움" };
    private static readonly Dictionary<string, string> PitchNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["직구"]="fastball",["포심"]="fastball",["포심패스트볼"]="fastball",["fastball"]="fastball",
        ["슬라이더"]="slider",["slider"]="slider",["커브"]="curve",["curve"]="curve",
        ["체인지업"]="changeup",["changeup"]="changeup",["포크"]="splitter",["포크볼"]="splitter",["스플리터"]="splitter",["splitter"]="splitter",
        ["투심"]="sinker",["투심패스트볼"]="sinker",["싱커"]="sinker",["sinker"]="sinker",["커터"]="cutter",["컷패스트볼"]="cutter",["cutter"]="cutter"
    };
    public DiamondRosterService(string databasePath) => _path = string.IsNullOrWhiteSpace(databasePath) ? "" : Path.GetFullPath(databasePath);

    public DiamondRoster Get(int? season, CancellationToken token = default)
    {
        if (season is < 1900 or > 2200) throw new DiamondInputError("올바른 시즌을 선택해 주세요.");
        _gate.Wait(token);
        try
        {
            var stamp = Stamp();
            var year = season ?? (_cache.Count > 0 ? _cache.Values.First().Roster.Seasons[0] : 0);
            if (_cache.TryGetValue(year, out var cached) && cached.Stamp == stamp && DateTime.UtcNow - cached.At < CacheLifetime)
                return cached.Roster;
            if (_cache.Values.Any(x => x.Stamp != stamp)) _cache.Clear();
            // A writer may commit while a cold roster is being read. Never cache its old data with the new file stamp.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested(); stamp = Stamp();
                var roster = Load(season, stamp, token);
                if (Stamp() != stamp) continue;
                while (_cache.Count >= 4 && !_cache.ContainsKey(roster.Season)) _cache.Remove(_cache.MinBy(x => x.Value.At).Key);
                _cache[roster.Season] = new(stamp, DateTime.UtcNow, roster);
                return roster;
            }
            throw new DiamondInputError("기록 DB를 갱신 중입니다. 잠시 후 선수 목록을 다시 불러와 주세요.", 503);
        }
        catch (OperationCanceledException) { throw; }
        catch (DiamondInputError) { throw; }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { _cache.Clear(); throw new DiamondInputError("기록실 DB의 선수 자료를 불러오지 못했습니다. DB 준비 상태를 확인해 주세요.", 503); }
        finally { _gate.Release(); }
    }

    public DiamondRosterSelection Select(int season, string batterId, string pitcherId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(batterId) || string.IsNullOrWhiteSpace(pitcherId) || batterId.Length > 80 || pitcherId.Length > 80)
            throw new DiamondInputError("기록실 선수 목록에서 타자와 투수를 선택해 주세요.");
        var roster = Get(season, token);
        var batter = roster.Batters.FirstOrDefault(x => x.Id == batterId);
        var pitcher = roster.Pitchers.FirstOrDefault(x => x.Id == pitcherId);
        if (batter == null || pitcher == null) throw new DiamondInputError("선택한 시즌의 타자와 투수를 다시 선택해 주세요.");
        // Match state owns its data; later roster refreshes cannot change an in-progress game's ratings.
        var selection = new DiamondRosterSelection(roster.Season, roster.AsOf, roster.Revision, batter, pitcher);
        return JsonSerializer.Deserialize<DiamondRosterSelection>(JsonSerializer.Serialize(selection, DiamondJson.Options), DiamondJson.Options)!;
    }

    private string Stamp()
    {
        if (_path.Length == 0 || !File.Exists(_path)) throw new DiamondInputError("기록실 DB가 아직 준비되지 않았습니다.", 503);
        static string Info(string p) { var f = new FileInfo(p); return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}:{f.CreationTimeUtc.Ticks}" : "absent"; }
        return Info(_path) + "|" + Info(_path + "-wal");
    }

    private DiamondRoster Load(int? requested, string stamp, CancellationToken token)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3 }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var seasons = new List<int>(); var dates = new Dictionary<int, string>();
        Read(connection, transaction, "SELECT SeasonYear,MAX(GameDate) FROM Games WHERE RoundCode='kbo_r' AND StatusCode='RESULT' AND AwayTeamCode NOT IN ('EA','WE') AND HomeTeamCode NOT IN ('EA','WE') GROUP BY SeasonYear ORDER BY SeasonYear DESC", 0, token, r =>
        { var y = r.GetInt32(0); if (y is >= 1900 and <= 2200) { seasons.Add(y); dates[y] = r.GetString(1); } });
        if (seasons.Count == 0) throw new DiamondInputError("기록실 DB에 완료된 정규시즌 경기가 없습니다.", 503);
        var year = requested ?? seasons[0];
        if (!seasons.Contains(year)) throw new DiamondInputError("수집된 정규시즌 중에서 선택해 주세요.");
        var version = "0";
        if (Has(connection, transaction, "Metadata", ["MetaKey", "MetaValue"], token))
            Read(connection, transaction, "SELECT MetaValue FROM Metadata WHERE MetaKey='DataVersion'", year, token, r => version = r.GetString(0));
        var profiles = Profiles(connection, transaction, year, token);
        var batters = new List<DiamondBatter>(); var pitchers = new List<DiamondPitcher>();
        var discipline = "SUM(Pitches) NP,SUM(InZone) Z,SUM(OutZone) OZ,SUM(ZoneSwings) ZS,SUM(ChaseSwings) OS,SUM(ZoneContacts) ZC,SUM(OutZoneContacts) OC";
        Read(connection, transaction, $"""
            WITH source AS (SELECT b.*,ROW_NUMBER() OVER(PARTITION BY b.Pcode ORDER BY g.GameDate DESC,g.GameId DESC,b.TeamCode) latest
              FROM BatterGameStats b JOIN Games g ON g.GameId=b.GameId WHERE {RegularGames} AND b.TeamCode NOT IN ('EA','WE'))
            SELECT Pcode,MAX(CASE WHEN latest=1 THEN Name END) Name,MAX(CASE WHEN latest=1 THEN TeamCode END) Team,
              SUM(PA) PA,SUM(AB) AB,SUM(H) H,SUM(HR) HR,SUM(SO) SO,SUM(TB) TB,SUM(BB) BB,SUM(HBP) HBP,SUM(SF) SF,{discipline}
            FROM source GROUP BY Pcode HAVING SUM(PA)>0 ORDER BY SUM(PA) DESC,Pcode
            """, year, token, r =>
        {
            var id = S(r,"Pcode"); var ab = N(r,"AB"); var pa = N(r,"PA"); var h = N(r,"H"); var slg = Ratio(N(r,"TB"),ab) ?? 0;
            var b = new DiamondBatter { Id=$"{year}:{id}",PlayerId=id,Name=S(r,"Name"),Team=S(r,"Team"),Pa=pa,Ab=ab,H=h,Hr=N(r,"HR"),So=N(r,"SO"),
                Avg=Ratio(h,ab)??0,Slg=slg,Ops=(Ratio(h+N(r,"BB")+N(r,"HBP"),ab+N(r,"BB")+N(r,"HBP")+N(r,"SF"))??0)+slg,
                Profile=profiles.GetValueOrDefault(id)??new(),Discipline=Discipline(r) };
            var notes = new List<string>(); if (pa<50) notes.Add("50타석 미만의 적은 표본"); if(ab==0) notes.Add("타수 없음: 타율·장타율 0 표시"); AddMissingNotes(notes,b.Profile,b.Discipline);
            b.SampleNote=Note(notes); batters.Add(b);
        });
        Read(connection, transaction, $"""
            WITH source AS (SELECT p.*,ROW_NUMBER() OVER(PARTITION BY p.Pcode ORDER BY g.GameDate DESC,g.GameId DESC,p.TeamCode) latest
              FROM PitcherGameStats p JOIN Games g ON g.GameId=p.GameId WHERE {RegularGames} AND p.TeamCode NOT IN ('EA','WE'))
            SELECT Pcode,MAX(CASE WHEN latest=1 THEN Name END) Name,MAX(CASE WHEN latest=1 THEN TeamCode END) Team,SUM(TBF) TBF,
              SUM(CASE WHEN HasFinalLine=1 THEN FinalBB ELSE PaBB END) BB,SUM(CASE WHEN HasFinalLine=1 THEN FinalSO ELSE PaSO END) SO,
              SUM(InningsOuts) Outs,SUM(HitsAllowed) H,SUM(EarnedRuns) ER,SUM(CASE WHEN HasFinalLine=1 THEN 0 ELSE 1 END) Missing,{discipline}
            FROM source GROUP BY Pcode HAVING SUM(TBF)>0 ORDER BY SUM(TBF) DESC,Pcode
            """, year, token, r =>
        {
            var id=S(r,"Pcode"); var outs=N(r,"Outs"); var missing=N(r,"Missing"); var bb=N(r,"BB"); var er=N(r,"ER");
            var p=new DiamondPitcher { Id=$"{year}:{id}",PlayerId=id,Name=S(r,"Name"),Team=S(r,"Team"),Tbf=N(r,"TBF"),Bb=bb,So=N(r,"SO"),Outs=outs,Er=er,
                Era=missing==0?Ratio(er*27,outs):null,Whip=missing==0?Ratio((N(r,"H")+bb)*3,outs):null,Profile=profiles.GetValueOrDefault(id)??new(),Discipline=Discipline(r) };
            var notes=new List<string>(); if(p.Tbf<50)notes.Add("50타자 미만의 적은 표본"); if(missing>0)notes.Add("최종 투수 기록 일부 미수집: ERA·WHIP 미표시"); AddMissingNotes(notes,p.Profile,p.Discipline);
            p.SampleNote=Note(notes); pitchers.Add(p);
        });
        if(batters.Count==0||pitchers.Count==0)throw new DiamondInputError("이 시즌에 게임에 사용할 타석·상대 타자 기록이 충분하지 않습니다.",503);
        Arsenals(connection, transaction, year, pitchers, token);
        var teams=batters.Select(x=>x.Team).Concat(pitchers.Select(x=>x.Team)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal)
            .Select(x=>new DiamondTeam(x,TeamNames.GetValueOrDefault(x,x))).ToArray();
        var revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"diamond-roster-v1|{year}|{version}|{stamp}"))).ToLowerInvariant();
        return new(year,seasons.ToArray(),dates[year],revision,teams,batters,pitchers);
    }

    private static Dictionary<string, DiamondProfile> Profiles(SqliteConnection c,SqliteTransaction tx,int year,CancellationToken token)
    {
        var profiles=new Dictionary<string,DiamondProfile>(StringComparer.Ordinal);
        if(Has(c,tx,"Players",["Pcode","BatsThrows"],token))
            Read(c,tx,"SELECT Pcode,BatsThrows FROM Players",year,token,r=>profiles[S(r,"Pcode")]=Profile(S(r,"BatsThrows")));
        // Prefer the selected season's hand information, not a player's later-season team/lineup metadata.
        if(Has(c,tx,"GamePlayers",["Pcode","HitType","GameId","TeamCode"],token))
            Read(c,tx,$"SELECT p.Pcode,p.HitType FROM GamePlayers p JOIN Games g ON g.GameId=p.GameId WHERE {RegularGames} AND p.HitType IS NOT NULL AND p.HitType<>'' ORDER BY g.GameDate,g.GameId,p.TeamCode",year,token,r=>profiles[S(r,"Pcode")]=Profile(S(r,"HitType")));
        var heightSources=new List<string>();
        foreach(var table in new[]{"BattingGameLines","PitchingGameLines"})
            if(Has(c,tx,table,["GameId","Pcode","Height"],token))heightSources.Add($"SELECT s.Pcode,CAST(s.Height AS REAL) Height,g.GameDate,g.GameId FROM {table} s JOIN Games g ON g.GameId=s.GameId WHERE {RegularGames} AND CAST(s.Height AS REAL)>0 AND CAST(s.Height AS REAL)<=250");
        if(heightSources.Count>0)
            Read(c,tx,"SELECT * FROM ("+string.Join(" UNION ALL ",heightSources)+") ORDER BY GameDate,GameId",year,token,r=>
            { var id=S(r,"Pcode"); if(!profiles.TryGetValue(id,out var profile))profiles[id]=profile=new(); profile.HeightCm=N(r,"Height"); });
        return profiles;
    }
    private static DiamondProfile Profile(string hands)=>new()
    { BatsThrows=hands,Throws=hands.StartsWith('좌')?"L":hands.StartsWith('우')?"R":null,
      Bats=hands.EndsWith("양타",StringComparison.Ordinal)?"S":hands.EndsWith("좌타",StringComparison.Ordinal)?"L":hands.EndsWith("우타",StringComparison.Ordinal)?"R":null,
      Delivery=hands.Contains('언')?"underhand":null };
    private static DiamondDiscipline Discipline(SqliteDataReader r)=>new()
    { ZonePitchRate=Ratio(N(r,"Z"),N(r,"Z")+N(r,"OZ")),ZoneSwingRate=Ratio(N(r,"ZS"),N(r,"Z")),ChaseRate=Ratio(N(r,"OS"),N(r,"OZ")),ZoneContactRate=Ratio(N(r,"ZC"),N(r,"ZS")),OutZoneContactRate=Ratio(N(r,"OC"),N(r,"OS")) };
    private static void AddMissingNotes(List<string> notes,DiamondProfile profile,DiamondDiscipline discipline)
    { if(profile.Throws==null||profile.Bats==null)notes.Add("투타 정보 미수집: 게임 기본 방향 적용"); if(discipline.ZonePitchRate==null||discipline.ZoneSwingRate==null||discipline.ChaseRate==null||discipline.ZoneContactRate==null||discipline.OutZoneContactRate==null)notes.Add("일부 선구안 표본 없음: 해당 게임 동작은 기본값 적용"); }
    private static double? Ratio(double n,double d)=>d>0?n/d:null;
    private static string? Note(IEnumerable<string> notes) { var s=string.Join(" · ",notes.Where(x=>!string.IsNullOrWhiteSpace(x)));return s.Length>0?s:null; }

    private static void Arsenals(SqliteConnection c,SqliteTransaction tx,int year,List<DiamondPitcher> pitchers,CancellationToken token)
    {
        var rows=new Dictionary<string,List<(string Name,double Count,double Speeds,double Sum)>>(StringComparer.Ordinal);
        if(Has(c,tx,"Pitches",["GameId","PitcherPcode","PitchType","SpeedKmh"],token))
            Read(c,tx,$"SELECT p.PitcherPcode,COALESCE(p.PitchType,'') Type,COUNT(*) NP,SUM(CASE WHEN p.SpeedKmh>0 THEN 1 ELSE 0 END) Speeds,SUM(CASE WHEN p.SpeedKmh>0 THEN p.SpeedKmh ELSE 0 END) SpeedSum FROM Pitches p JOIN Games g ON g.GameId=p.GameId WHERE {RegularGames} GROUP BY p.PitcherPcode,p.PitchType",year,token,r=>
            { var id=S(r,"PitcherPcode");if(!rows.TryGetValue(id,out var list))rows[id]=list=[];list.Add((S(r,"Type").Trim(),N(r,"NP"),N(r,"Speeds"),N(r,"SpeedSum"))); });
        foreach(var p in pitchers)
        {
            var notes=new List<string>(); if(p.SampleNote!=null)notes.Add(p.SampleNote);
            if(rows.TryGetValue(p.PlayerId,out var list))
            {
                var total=list.Sum(x=>x.Count);
                var usable=list.Where(x=>PitchNames.ContainsKey(x.Name)).GroupBy(x=>PitchNames[x.Name])
                    .Select(g=>new {Type=g.Key,Count=g.Sum(x=>x.Count),Speeds=g.Sum(x=>x.Speeds),Sum=g.Sum(x=>x.Sum)}).Where(x=>x.Speeds>0)
                    .OrderByDescending(x=>x.Count).ThenBy(x=>x.Type,StringComparer.Ordinal).Select(x=>new DiamondArsenal(x.Type,x.Sum/x.Speeds,100*x.Count/total)).ToArray();
                if(usable.Length>0){p.Arsenal=usable;p.ArsenalSource="observed";}
                if(list.Any(x=>!PitchNames.ContainsKey(x.Name)))notes.Add("미지원·미상 구종은 게임에서 제외; 구사율 분모에는 포함");
                if(list.Any(x=>x.Speeds<x.Count))notes.Add("구속 미측정 투구는 평균 구속에서 제외");
            }
            if(p.Arsenal.Count==0)
            { p.Arsenal=[new("fastball",145,55),new("slider",133,30),new("curve",122,15)];p.ArsenalSource="default";notes.Add("사용 가능한 구종·구속 자료 없음: 게임 기본 구종·구속(실측 아님)"); }
            p.SampleNote=Note(notes);
        }
    }

    private static string S(SqliteDataReader r,string name)=>r[name] is DBNull?"":Convert.ToString(r[name],CultureInfo.InvariantCulture)??"";
    private static double N(SqliteDataReader r,string name)=>r[name] is DBNull?0:Convert.ToDouble(r[name],CultureInfo.InvariantCulture);
    private static bool Has(SqliteConnection c,SqliteTransaction tx,string table,string[] columns,CancellationToken token)
    { var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Read(c,tx,$"PRAGMA table_info({table})",0,token,r=>names.Add(r.GetString(1)));return columns.All(names.Contains); }
    private static void Read(SqliteConnection c,SqliteTransaction tx,string sql,int year,CancellationToken token,Action<SqliteDataReader> row)
    {
        token.ThrowIfCancellationRequested();using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;cmd.CommandTimeout=8;
        if(sql.Contains("$year",StringComparison.Ordinal))cmd.Parameters.AddWithValue("$year",year);
        using var registration=token.Register(cmd.Cancel);using var reader=cmd.ExecuteReader();
        while(reader.Read()){token.ThrowIfCancellationRequested();row(reader);}token.ThrowIfCancellationRequested();
    }
}
