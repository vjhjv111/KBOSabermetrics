using System.Reflection;
using NaverRelay.Parsing;
using NaverRelay.Infrastructure.Sqlite;

void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
var classify=typeof(RelayParser).Assembly.GetType("NaverRelay.Parsing.BatterResultClassifier")!.GetMethod("Classify",BindingFlags.Static|BindingFlags.Public)!;
BattingOutcome Outcome(string text)=>(BattingOutcome)classify.Invoke(null,[text])!;
foreach(var text in new[]{"스트라이크 낫아웃 폭투","스트라이크 낫아웃 포일","포수 스트라이크 낫아웃 다른주자 수비로 출루","포수 스트라이크 낫아웃 실책으로 출루"}){
    var o=Outcome("선수 : "+text);Check(o.IsStrikeout&&o.CountsAsAtBat&&!o.IsOut&&o.ReachedBase&&!o.IsHit,text);
}
foreach(var text in new[]{"포수 스트라이크 낫 아웃 (포수 태그아웃)","포수 쓰리번트 아웃","삼진 아웃"}){var o=Outcome("선수 : "+text);Check(o.IsStrikeout&&o.IsOut&&!o.ReachedBase,text);}
Check(!Outcome("선수 : 3루수 희생번트 아웃").IsStrikeout,"희생번트는 삼진 아님");

var game=new NormalizedGame{GameId="20260601HHSS02026",GameDate="2026-06-01",RoundCode="kbo_r",AwayTeam=new(){TeamCode="HH"},HomeTeam=new(){TeamCode="SS"}};
game.PlateAppearances.Add(new PlateAppearance{Inning=2,BattingTeamCode="SS",BatOrder=3,BatterName="테스트",Outcome=Outcome("테스트 : 유격수 실책으로 출루")});
game.BattingLines.Add(new GamePlayerBattingLine{TeamCode="SS",Name="테스트",Hits=1});
var notice=new KboCorrection("2026:0:1",2026,"2026-06-01","한화:삼성","2말","3","테스트\n(투수)","실책","안타","삼성\n테스트(안타 1→2, 루타수 1→2)","06/02");
DatabaseCacheService.ApplyCorrection(game,notice);Check(game.PlateAppearances[0].Outcome.IsHit&&game.BattingLines[0].Hits==2,"공식 전후값 적용");
DatabaseCacheService.ApplyCorrection(game,notice);Check(game.BattingLines[0].Hits==2&&game.PlateAppearances[0].Outcome.TotalBases==1,"정정 재실행 멱등성");
game.BattingLines[0].Hits=5;DatabaseCacheService.ApplyCorrection(game,notice);Check(game.BattingLines[0].Hits==5&&game.Diagnostics.Any(d=>d.Code=="KBO_CORRECTION_PENDING"),"예상과 다른 수치 보존 및 검토 표시");
game.PlateAppearances.Add(game.PlateAppearances[0]);game.Diagnostics.Clear();DatabaseCacheService.ApplyCorrection(game,notice);Check(game.Diagnostics.Any(d=>d.Code=="KBO_CORRECTION_PENDING"),"타석 매칭 모호성 차단");

if(args.Length>0){
    foreach(var (id,expected) in new[]{("20260513NCLT02026",82),("20260612SKSS02026",0)}){
        var g=RelayParser.ParseFile(Path.Combine(args[0],id+".json"));
        if(expected>0)Check(g.PlateAppearances.Count(p=>p.IsOfficialPlateAppearance)==expected,"반복 중계 구간 제거");
        else Check(g.PlateAppearances.Count(p=>p.BatterName=="박계범"&&p.Outcome.IsStrikeout)==1,"동일 투구 ID 타석 중복 제거");
    }
    foreach(var (id,name) in new[]{("20260418SKNC02026","고명준"),("20260617HHNC02026","천재환"),("20260902HHKT02026","장준원"),("20260908OBHH02026","이도윤")}){
        var g=RelayParser.ParseFile(Path.Combine(args[0],id+".json"));Check(g.Diagnostics.Any(d=>d.Code=="TWO_STRIKE_SUBSTITUTION"&&d.Message.Contains(name)),"2스트라이크 교체 귀속 "+id);
    }
}
