using System.Text.Json;
using System.Text.Json.Nodes;
using NaverRelay.Parsing;

internal static class IdentityRecoveryTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
    public static void Run(string folder)
    {
        foreach(var file in new[]{"20260912HTKT02026.json","20260912LGSS02026.json","20260912LTWO02026.json","20260912NCOB02026.json"})
        {
            var futureJson=File.ReadAllText(Path.Combine(folder,file));bool deferred=false;
            try{RelayParser.ParseJson(futureJson);}catch(GameNotStartedException){deferred=true;}
            Check(deferred,"명시적인 미시작 빈 경기는 오류 대신 수집 보류 "+file);
            var corrupt=JsonNode.Parse(futureJson)!;corrupt["naver"]!["result"]!["game"]!["statusCode"]="RESULT";bool invalid=false;
            try{RelayParser.ParseJson(corrupt.ToJsonString());}catch(InvalidDataException){invalid=true;}
            Check(invalid,"종료 경기의 중계 누락은 미시작으로 숨기지 않음 "+file);
        }
        var path=Path.Combine(folder,"20260505WOSS02026.json");var json=File.ReadAllText(path);
        NormalizedGame Parse(){var g=RelayParser.ParseJson(json);KboPlayLog.Apply(g,g.ImportedOfficialSource!);return g;}
        var original=Parse();var doc=original.ImportedOfficialSource!;var team=new[]{doc.BoxScore!.Away,doc.BoxScore.Home}.Single(t=>t.Batting.Any(b=>b.Identity?.NeedsLineupEvidence==true));
        var target=team.Batting.First(b=>b.Identity?.NeedsLineupEvidence==true);var game=Parse();
        foreach(var line in game.BattingLines.Where(b=>b.TeamCode==team.TeamCode&&b.Name==target.Name))
        {line.AtBats=target.Stats.AtBats;line.Hits=target.Stats.Hits;line.HomeRuns=target.Stats.HomeRuns;line.Walks=target.Stats.Walks;line.Strikeouts=target.Stats.Strikeouts;}
        Reject(game,doc,"동명이인 후보의 최종 기록이 같으면 순서만으로 ID를 추정하지 않음");
        game=Parse();var anchors=game.BattingLines.Where(b=>b.TeamCode==team.TeamCode&&b.Name!=target.Name).Take(2).ToArray();
        (anchors[0].BatOrder,anchors[1].BatOrder)=(anchors[1].BatOrder,anchors[0].BatOrder);
        (anchors[0].LineupSequence,anchors[1].LineupSequence)=(anchors[1].LineupSequence,anchors[0].LineupSequence);
        Reject(game,doc,"공식 표와 네이버 확정 선수들의 타순/교체순서 불일치 거부");
        foreach(var mode in new[]{"missing","foreign","duplicate"})
        {
            var root=JsonNode.Parse(json)!;var table=root["kboOfficial"]!["boxScore"]![team.TeamCode==doc.BoxScore.Away.TeamCode?"away":"home"]!["batting"]!;
            var identity=table["rowIdentities"]!.AsArray().First(x=>x!["matchStatus"]!.GetValue<string>()=="ambiguous_kbo_rows")!;
            if(mode=="missing")identity["candidatePcodes"]!.AsArray().RemoveAt(1);
            else if(mode=="foreign")identity["candidatePcodes"]![0]="00000";
            else identity["candidatePcodes"]![1]=identity["candidatePcodes"]![0]!.GetValue<string>();
            bool rejected=false;try{var g=RelayParser.ParseJson(root.ToJsonString());KboPlayLog.Apply(g,g.ImportedOfficialSource!);KboBoxScore.Apply(g,g.ImportedOfficialSource!.BoxScore!,g.ImportedOfficialSource.GameId);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"미해결 선수 후보 목록 위조/누락 거부 "+mode);
        }
        void Reject(NormalizedGame g,KboPlayLog.Document official,string message)
        {
            var before=JsonSerializer.Serialize(new{g.BattingLines,g.PitchingLines});bool rejected=false;
            try{KboBoxScore.Apply(g,official.BoxScore!,official.GameId);}catch(InvalidDataException){rejected=true;}
            Check(rejected&&before==JsonSerializer.Serialize(new{g.BattingLines,g.PitchingLines}),message+" / 최종 기록 변경 없음");
        }
    }
}
