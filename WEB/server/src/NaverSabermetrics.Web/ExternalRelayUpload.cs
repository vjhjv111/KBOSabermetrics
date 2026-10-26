using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NaverRelayUI.Collection;

namespace NaverSabermetrics.Web;

public static class ExternalRelayUpload
{
    public const string Route="/api/collector/relay";
    public const int MaxBytes=8*1024*1024;
    private static readonly SemaphoreSlim Writer=new(1,1);
    public static void Authorize(string supplied)
    {
        var expected=Environment.GetEnvironmentVariable("FANZAI_COLLECTOR_KEY");
        if(string.IsNullOrWhiteSpace(expected)||expected.Length<32)throw new RequestError("외부 수집 미설정",503,"COLLECTOR_DISABLED");
        if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            throw new RequestError("수집 인증 실패",401,"COLLECTOR_AUTH");
    }
    public static async Task<IResult> ReceiveAsync(HttpContext context,RenderCollectorOptions options)
    {
        Authorize(context.Request.Headers["X-Fanzai-Collector-Key"].ToString());
        if(!await Writer.WaitAsync(0,context.RequestAborted))throw new RequestError("수집 저장 중",429,"COLLECTOR_BUSY");
        try {
            using var bytes=new MemoryStream();
            var buffer=new byte[8192];
            int count;
            while((count=await context.Request.Body.ReadAsync(buffer,context.RequestAborted))>0) {
                if(bytes.Length+count>MaxBytes)throw new RequestError("수집 파일 크기 초과",413,"COLLECTOR_SIZE");
                bytes.Write(buffer,0,count);
            }
            JsonNode root;
            try {root=JsonNode.Parse(bytes.ToArray())??throw new FormatException();}
            catch(Exception ex) when(ex is System.Text.Json.JsonException or FormatException){throw new RequestError("잘못된 중계 JSON");}
            var id=root["result"]?["game"]?["gameId"]?.ToString()??"";
            HomeLiveGame? game;
            try {game=HomeLiveService.Parse(root.ToJsonString(),id);}
            catch(Exception ex) when(ex is FormatException or InvalidOperationException){throw new RequestError("잘못된 경기 데이터");}
            var today=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
            if(game is null || game.RoundCode?.StartsWith("kbo_ps",StringComparison.Ordinal)!=true ||
                !DateTime.TryParse(game.Date,out var date) || date<today.AddDays(-2)||date>today.AddDays(1))
                throw new RequestError("최근 포스트시즌 경기만 업로드할 수 있습니다.");
            if(!DateTimeOffset.TryParse(root["collectedAt"]?.ToString(),out var fetched) ||
                fetched<DateTimeOffset.UtcNow.AddMinutes(-10)||fetched>DateTimeOffset.UtcNow.AddMinutes(1))
                throw new RequestError("수집 시각이 오래되었거나 잘못되었습니다.");
            Directory.CreateDirectory(options.JsonDirectory);
            var path=Path.Combine(options.JsonDirectory,date.ToString("yyyyMMdd")+"_"+id+".json");
            if(File.Exists(path)) {
                var old=JsonNode.Parse(await File.ReadAllTextAsync(path,context.RequestAborted));
                if(DateTimeOffset.TryParse(old?["collectedAt"]?.ToString(),out var stamp)&&stamp>fetched)
                    return Results.Ok(new {id,ignored=true});
                var previous=HomeLiveService.Parse(old!.ToJsonString(),id);
                if(previous?.Final==true&&!game.Final)return Results.Ok(new{id,ignored=true});
            }
            root["externalReceivedAt"]=DateTimeOffset.UtcNow;
            await UnifiedDocumentStore.SaveAtomicAsync(path,root.ToJsonString(),context.RequestAborted);
            return Results.Ok(new{id,status=game.Status,away=game.AwayScore,home=game.HomeScore});
        } finally {Writer.Release();}
    }
}
