using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

public sealed record DiamondSeasonMatchInfo(string Code, string Mode, string Team, string HostTeam, string GuestTeam, long Version, bool Waiting, long ExpiresAt);
public sealed record DiamondSeasonMatchResponse(DiamondSeasonView Save, DiamondView? Action, long ServerNow, DiamondSeasonMatchInfo Match, IReadOnlyDictionary<string, DiamondCareerAppearance> Appearances);
public sealed class DiamondSeasonFriendMatch
{
    public string Code { get; set; } = ""; public string Mode { get; set; } = "pvp";
    public string Host { get; set; } = ""; public string? Guest { get; set; }
    public string HostTeam { get; set; } = ""; public string GuestTeam { get; set; } = "";
    public long Version { get; set; } public long ExpiresAt { get; set; }
    public DiamondSeasonSave Save { get; set; } = new();
    public Dictionary<string, DiamondCareerAppearance> Appearances { get; set; } = [];
}
/// <summary>Dedicated friendly-match facade. League rows and career rewards are never changed.</summary>
public sealed class DiamondSeasonMatchService(DiamondSeasonService seasons)
{
    public string DatabasePath => seasons.DatabasePath;
    public long Now() => seasons.Now();
    public DiamondSeasonMatchResponse Get(string? code, string actor, CancellationToken token = default) => seasons.GetMatch(code, actor, token);
    public DiamondSeasonMatchResponse Post(JsonElement body, string actor, string ip, CancellationToken token = default) => seasons.PostMatch(body, actor, ip, token);
}
public sealed partial class DiamondSeasonService
{
    private DiamondSeasonFriendMatch LoadMatch(SqliteConnection c, SqliteTransaction tx, string? code)
    {
        code = code?.Trim().ToUpperInvariant();
        if (code == null || !Regex.IsMatch(code, "^F[A-Z2-9]{7}$", RegexOptions.CultureInvariant)) throw new DiamondInputError("8자리 친선 경기 코드를 확인해 주세요.");
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT State,Version,ExpiresAt FROM DiamondSeasonMatches WHERE Code=$code";
        cmd.Parameters.AddWithValue("$code", code); using var r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt64(2) <= Now()) throw new DiamondInputError("친선 경기가 없거나 만료되었습니다.", 404);
        var match = JsonSerializer.Deserialize<DiamondSeasonFriendMatch>(r.GetString(0), DiamondJson.Options)!;
        match.Version = r.GetInt64(1); match.Save.Version = match.Version; match.ExpiresAt = r.GetInt64(2);
        if (match.Save.Game != null) match.Save.Game.Duel.ExpiresAt = match.ExpiresAt;
        return match;
    }
    private void StoreMatch(SqliteConnection c, SqliteTransaction tx, DiamondSeasonFriendMatch match)
    {
        match.Version++; match.Save.Version = match.Version; match.Save.UpdatedAt = Now(); match.ExpiresAt = Now() + 86400000;
        if (match.Save.Game != null) match.Save.Game.Duel.ExpiresAt = match.ExpiresAt;
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO DiamondSeasonMatches(Code,Owner,State,Version,ExpiresAt) VALUES($code,$owner,$state,$version,$expires) ON CONFLICT(Code) DO UPDATE SET State=excluded.State,Version=excluded.Version,ExpiresAt=excluded.ExpiresAt";
        cmd.Parameters.AddWithValue("$code",match.Code); cmd.Parameters.AddWithValue("$owner",match.Host);
        cmd.Parameters.AddWithValue("$state",JsonSerializer.Serialize(match,DiamondJson.Options));cmd.Parameters.AddWithValue("$version",match.Version);cmd.Parameters.AddWithValue("$expires",match.ExpiresAt);cmd.ExecuteNonQuery();
    }
    private static string MatchTeam(DiamondSeasonFriendMatch m, string actor)
    {
        if (m.Host == actor) return m.HostTeam;
        if (m.Guest == actor) return m.GuestTeam;
        throw new DiamondInputError("먼저 경기 코드로 참가해 주세요.",403);
    }
    private DiamondSeasonMatchResponse MatchView(DiamondSeasonFriendMatch match, string actor)
    {
        var team = MatchTeam(match, actor); var response = View(match.Save, match.Host);
        var waiting = match.Mode == "pvp" && match.Guest == null;
        var action = response.Action;
        if (action != null) action = action with { Mode = match.Mode, Waiting = waiting, ExpiresAt = match.ExpiresAt,
            Role = DiamondSeasonRules.BattingTeam(match.Save.Game!) == team ? "batter" : "pitcher",
            Score = match.Save.Game!.HomeTeam == team ? match.Save.Game.HomeRuns : match.Save.Game.AwayRuns };
        return new(response.Save! with { Team = team, Version = match.Version }, action, Now(),
            new(match.Code,match.Mode,team,match.HostTeam,match.GuestTeam,match.Version,waiting,match.ExpiresAt), match.Appearances);
    }
    public DiamondSeasonMatchResponse GetMatch(string? code, string actor, CancellationToken token = default)
    {
        CheckActor(actor); token.ThrowIfCancellationRequested();
        using var c = Open(); using var tx = c.BeginTransaction(); var match = LoadMatch(c,tx,code); MatchTeam(match,actor);
        if (TickMatch(match, Now())) { FinishFriendly(match); StoreMatch(c,tx,match); }
        else if (match.ExpiresAt < Now() + 23 * 3600000L)
        {
            // Renew active rooms without changing the input version on every viewer poll.
            match.ExpiresAt = Now() + 86400000; if (match.Save.Game != null) match.Save.Game.Duel.ExpiresAt = match.ExpiresAt;
            using var touch = c.CreateCommand(); touch.Transaction = tx;
            touch.CommandText = "UPDATE DiamondSeasonMatches SET ExpiresAt=$expires WHERE Code=$code";
            touch.Parameters.AddWithValue("$expires",match.ExpiresAt);touch.Parameters.AddWithValue("$code",match.Code);touch.ExecuteNonQuery();
        }
        tx.Commit(); return MatchView(match,actor);
    }
    public DiamondSeasonMatchResponse PostMatch(JsonElement body, string actor, string ip, CancellationToken token = default)
    {
        CheckActor(actor); token.ThrowIfCancellationRequested();
        if(body.ValueKind!=JsonValueKind.Object)throw new DiamondInputError("친선 경기 요청을 확인해 주세요.");
        var op=Text(body,"op");var request=Text(body,"requestId");
        if(op is not("create" or "join" or "pitch" or "swing" or "take" or "tick" or "substitute" or "change-pitcher" or "ready" or "sim-half" or "sim-game"))
            throw new DiamondInputError("지원하지 않는 친선 경기 조작입니다.");
        if(request==null||!Regex.IsMatch(request,"^[A-Za-z0-9_-]{8,80}$",RegexOptions.CultureInvariant))throw new DiamondInputError("요청 식별자를 확인해 주세요.");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())));
        // Idempotent retries must not depend on the source warehouse or invoke roster hooks twice.
        using(var c=Open())using(var tx=c.BeginTransaction(deferred:true))
        {
            using var prior=c.CreateCommand();prior.Transaction=tx;prior.CommandText="SELECT Hash,Code FROM DiamondSeasonMatchRequests WHERE Owner=$owner AND RequestId=$request";
            prior.Parameters.AddWithValue("$owner",actor);prior.Parameters.AddWithValue("$request",request);
            string? oldHash=null,oldCode=null;using(var r=prior.ExecuteReader()){if(r.Read()){oldHash=r.GetString(0);oldCode=r.GetString(1);}}
            if(oldHash!=null){if(oldHash!=hash)throw new DiamondInputError("같은 요청의 내용이 달라졌습니다.",409);var old=LoadMatch(c,tx,oldCode);tx.Commit();return MatchView(old,actor);}tx.Commit();
        }
        DiamondRoster? roster=null;DiamondSeasonRosterOverride? custom=null;DiamondPitchingProfile? profile=null;
        if(op=="create")
        {
            roster=_roster.Get(Integer(body,"season"),token);custom=_rosterOverride?.Invoke(actor,roster.Season);
        }
        else if(op is "join" or "ready")
        {
            DiamondSeasonFriendMatch preview;
            using(var c=Open())using(var tx=c.BeginTransaction(deferred:true)){preview=LoadMatch(c,tx,Text(body,"code"));tx.Commit();}
            if(op=="join")custom=_rosterOverride?.Invoke(actor,preview.Save.Season);
            else if(preview.Mode=="ai"&&preview.Host==actor&&preview.Save.Game is {Complete:false} g&&g.Duel.Pitch is not {Resolved:false})
            {
                AutoRelieve(preview.Save,g,false);var id=DiamondSeasonRules.Pitcher(g);
                if(g.Duel.PitchingProfile?.PitcherId!=id)
                {
                    try { if(!id.Contains(":career_",StringComparison.Ordinal)){var observed=_roster.SelectPitching(preview.Save.Season,id,token);if(observed.Revision==preview.Save.Revision)profile=observed;} }
                    catch(DiamondInputError e)when(e.Status is 400 or 503){}
                    profile??=new(){Season=preview.Save.Season,PitcherId=id,Revision=preview.Save.Revision,Source="default",SampleNote="저장한 성적·구종·구속 기준입니다. 같은 기준일의 코스 분포가 없어 게임 모델을 사용합니다."};
                }
            }
        }
        using var connection=Open();using var transaction=connection.BeginTransaction();
        // Recheck under the write lock so parallel identical create/join requests cannot duplicate work.
        using(var prior=connection.CreateCommand())
        {
            prior.Transaction=transaction;prior.CommandText="SELECT Hash,Code FROM DiamondSeasonMatchRequests WHERE Owner=$owner AND RequestId=$request";
            prior.Parameters.AddWithValue("$owner",actor);prior.Parameters.AddWithValue("$request",request);
            string? oldHash=null,oldCode=null;using(var r=prior.ExecuteReader()){if(r.Read()){oldHash=r.GetString(0);oldCode=r.GetString(1);}}
            if(oldHash!=null){if(oldHash!=hash)throw new DiamondInputError("같은 요청의 내용이 달라졌습니다.",409);var old=LoadMatch(connection,transaction,oldCode);transaction.Commit();return MatchView(old,actor);}
        }
        DiamondSeasonFriendMatch match;
        if(op=="create")
        {
            var hostTeam=Text(body,"hostTeam")??"";var guestTeam=Text(body,"guestTeam")??"";var mode=Text(body,"mode")??"pvp";
            if(hostTeam==guestTeam||mode is not("ai" or "pvp"))throw new DiamondInputError("서로 다른 두 팀과 대결 모드를 선택해 주세요.");
            var translated=JsonSerializer.SerializeToNode(body)!.AsObject();translated["team"]=hostTeam;translated["seriesPerPair"]=1;
            var save=Create(JsonSerializer.SerializeToElement(translated,DiamondJson.Options),roster!,custom,actor,opponentTeam:guestTeam);
            if(save.Teams.All(x=>x.Code!=guestTeam))throw new DiamondInputError("상대 팀을 선택해 주세요.");
            LimitMatchCreation(connection,transaction,actor,ip);
            var code=NewMatchCode(connection,transaction);
            save.Id=code;save.Teams=save.Teams.Where(x=>x.Code==hostTeam||x.Code==guestTeam).ToList();save.TotalDays=1;
            save.Schedule=[new(){Id=code+":friendly",Day=1,HomeTeam=hostTeam,AwayTeam=guestTeam}];
            save.Game=StartGame(save,save.Schedule[0],actor);save.Game.Duel.Mode=mode;
            match=new(){Code=code,Host=actor,HostTeam=hostTeam,GuestTeam=guestTeam,Mode=mode,Save=save};
            CaptureMatchAppearance(match,custom);
        }
        else
        {
            match=LoadMatch(connection,transaction,Text(body,"code"));
            if(op=="join")
            {
                if(match.Mode!="pvp")throw new DiamondInputError("친구 대결 코드가 아닙니다.");
                if(match.Host!=actor&&match.Guest!=actor)
                {
                    if(match.Guest!=null)throw new DiamondInputError("이미 두 팀이 참가했습니다.",409);
                    match.Guest=actor;match.Save.Game!.Duel.Guest=actor;
                    if(custom!=null)
                    {
                        CaptureMatchAppearance(match,custom);
                        var temporary=new DiamondSeasonSave{Season=match.Save.Season,Team=match.GuestTeam,Teams=match.Save.Teams};ApplyCustom(temporary,custom);
                        var team=match.Save.Teams.Single(x=>x.Code==match.GuestTeam);var g=match.Save.Game;
                        g.AwayLineup=[..team.Lineup];g.AwayUsedBatters=[..team.Lineup];
                        if(temporary.CharacterPitcherId!=null){g.AwayPitcher=temporary.CharacterPitcherId;g.AwayUsedPitchers=[g.AwayPitcher];}
                        BindMatchup(match.Save,g,match.Host);
                    }
                }
            }
            else
            {
                MatchTeam(match,actor);
                if(Integer(body,"version")!=match.Version)throw new DiamondInputError("상대 입력으로 경기가 바뀌었습니다. 최신 상태에서 다시 시도해 주세요.",409);
                if(match.Mode=="pvp"&&match.Guest==null)throw new DiamondInputError("상대 팀의 참가를 기다려 주세요.",409);
                if(profile!=null)match.Save.Game!.Duel.PitchingProfile=profile;
                ApplyFriendly(match,body,op,actor,token);
            }
        }
        token.ThrowIfCancellationRequested();StoreMatch(connection,transaction,match);
        using var remember=connection.CreateCommand();remember.Transaction=transaction;
        remember.CommandText="INSERT INTO DiamondSeasonMatchRequests(Owner,RequestId,Hash,Code) VALUES($owner,$request,$hash,$code)";
        remember.Parameters.AddWithValue("$owner",actor);remember.Parameters.AddWithValue("$request",request);remember.Parameters.AddWithValue("$hash",hash);remember.Parameters.AddWithValue("$code",match.Code);remember.ExecuteNonQuery();
        transaction.Commit();return MatchView(match,actor);
    }
    private static string NewMatchCode(SqliteConnection c,SqliteTransaction tx)
    {
        const string alphabet="ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for(var n=0;n<8;n++)
        {
            var code="F"+string.Concat(RandomNumberGenerator.GetBytes(7).Select(x=>alphabet[x%32]));
            using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT COUNT(*) FROM DiamondSeasonMatches WHERE Code=$code";cmd.Parameters.AddWithValue("$code",code);
            if(Convert.ToInt32(cmd.ExecuteScalar())==0)return code;
        }
        throw new DiamondInputError("경기 코드가 겹쳤습니다. 다시 시도해 주세요.",409);
    }
    private static void CaptureMatchAppearance(DiamondSeasonFriendMatch match, DiamondSeasonRosterOverride? custom)
    {
        if (custom?.Appearance == null) return;
        // Snapshot only the participating avatar's visual fields, never the career or owner record.
        if (custom.Batter is { } batter) match.Appearances[batter.Id] = Clone(custom.Appearance);
        if (custom.Pitcher is { } pitcher) match.Appearances[pitcher.Id] = Clone(custom.Appearance);
    }
    private void LimitMatchCreation(SqliteConnection c,SqliteTransaction tx,string actor,string ip)
    {
        var minute=Now()/60000;var ipHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
        foreach(var (key,limit) in new[]{($"actor:{actor}:{minute}",8),($"ip:{ipHash}:{minute}",20),($"global:{minute}",150)})
        {
            using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO DiamondSeasonMatchLimits(Key,Count,ExpiresAt) VALUES($key,1,$expires) ON CONFLICT(Key) DO UPDATE SET Count=Count+1 RETURNING Count";
            cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$expires",(minute+2)*60000);
            if(Convert.ToInt32(cmd.ExecuteScalar())>limit)throw new DiamondInputError("친선 경기 생성이 많습니다. 1분 후 다시 시도해 주세요.",429);
        }
        using var clean=c.CreateCommand();clean.Transaction=tx;
        clean.CommandText="DELETE FROM DiamondSeasonMatchLimits WHERE ExpiresAt<$now; DELETE FROM DiamondSeasonMatchRequests WHERE Code IN(SELECT Code FROM DiamondSeasonMatches WHERE ExpiresAt<$now LIMIT 50); DELETE FROM DiamondSeasonMatches WHERE Code IN(SELECT Code FROM DiamondSeasonMatches WHERE ExpiresAt<$now LIMIT 50)";
        clean.Parameters.AddWithValue("$now",Now());clean.ExecuteNonQuery();
    }
    private bool TickMatch(DiamondSeasonFriendMatch match,long now)
    {
        if(match.Mode=="ai")return Tick(match.Save,match.Host,now);
        if(match.Guest==null||match.Save.Game is not {Complete:false} g||g.Duel.Pitch is not {Resolved:false} pitch||now<=pitch.ReleaseAt+pitch.FlightMs+750)return false;
        Resolve(match.Save,g,null,now);return true;
    }
    private static void FinishFriendly(DiamondSeasonFriendMatch match)
    {
        if(match.Save.Game is not {Complete:true} g)return;
        RecordGame(match.Save,g);match.Save.Complete=true;match.Save.Day=2;
    }
    private void ApplyFriendly(DiamondSeasonFriendMatch match,JsonElement body,string? op,string actor,CancellationToken token)
    {
        var save=match.Save;var game=save.Game!;
        if(match.Mode=="pvp"&&op is "ready" or "sim-half" or "sim-game")throw new DiamondInputError("친구 대결은 양쪽 팀이 직접 조작합니다.",403);
        if(game.Complete)return;
        if(TickMatch(match,Now())){FinishFriendly(match);return;}
        if(op=="tick")return;
        var team=MatchTeam(match,actor);var batting=DiamondSeasonRules.BattingTeam(game)==team;
        if(op is "substitute" or "change-pitcher")
        {
            var ownerTeam=save.Team;try{save.Team=team;Substitute(save,game,body,op);}finally{save.Team=ownerTeam;}return;
        }
        if(op is "sim-half" or "sim-game")
        {
            if(match.Mode!="ai")throw new DiamondInputError("친구 대결은 양쪽 팀이 직접 조작합니다.",403);
            Simulate(save,game,match.Host,op=="sim-half",token);FinishFriendly(match);return;
        }
        if(op is "pitch" or "ready")
        {
            if(op=="ready"&&!(match.Mode=="ai"&&batting)||op=="pitch"&&batting)throw new DiamondInputError("현재 내 팀의 공격·수비에 맞는 조작을 사용해 주세요.",403);
            var previous=Integer(body,"previousPitch");if(previous<game.Duel.PitchCount)return;if(previous>game.Duel.PitchCount)throw new DiamondInputError("현재 투구를 확인해 주세요.",409);
            if(game.Duel.Pitch is{Resolved:false})return;
            if(game.Duel.Pitch is{} last&&Now()<Math.Max(last.ReleaseAt+last.FlightMs,last.Reaction?.At??0)+900)throw new DiamondInputError("다음 투구를 준비하고 있습니다.",409);
            if(op=="ready")NewPitch(save,game,match.Host,null,null,.85,false);
            else{var q=Number(body,"quality");if(q is<0 or>1)throw new DiamondInputError("릴리스 입력을 확인해 주세요.");NewPitch(save,game,match.Host,Text(body,"type")??"",Aim(body),q,false);}return;
        }
        if(op is "swing" or "take")
        {
            if(!batting)throw new DiamondInputError("타격 중인 팀만 스윙할 수 있습니다.",403);
            var pitch=game.Duel.Pitch??throw new DiamondInputError("진행 중인 투구가 없습니다.");if(pitch.Resolved)return;
            DiamondSwing? swing=null;
            if(op=="swing")
            {
                var id=Integer(body,"pitchId");if(id<pitch.Id)return;if(id!=pitch.Id)throw new DiamondInputError("현재 공을 확인해 주세요.",409);
                var at=body.TryGetProperty("inputAt",out _)?Number(body,"inputAt"):Number(body,"at")-DiamondEngine.SwingContactMs;
                if(at<Now()-1500||at>Now()+150)throw new DiamondInputError("연결 지연이 큽니다. 최신 투구에서 다시 시도해 주세요.",409);
                swing=new(at+DiamondEngine.SwingContactMs,Aim(body));
            }
            else if(Now()<pitch.ReleaseAt+pitch.FlightMs)return;
            Resolve(save,game,swing,Now());FinishFriendly(match);return;
        }
        throw new DiamondInputError("지원하지 않는 친선 경기 조작입니다.");
    }
}
