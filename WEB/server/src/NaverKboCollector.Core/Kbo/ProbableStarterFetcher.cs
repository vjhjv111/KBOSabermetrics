using System.Text.Json;
using System.Text.Json.Nodes;

namespace KboRelayDownloader;

// 경기 당일 KBO 라인업 패널(LineupPreviewParser)이 열리기(보통 경기 1~2시간 전) 한참 전에 먼저
// 발표되는 "예고선발"을 네이버 game-polling API에서 가져옵니다. 이 API의 응답에서
// textRelayData(그날 엔트리 전체 명단)는 라인업 패널과 비슷한 시점에야 채워지지만,
// result.game.homeStarterName/awayStarterName 두 필드는 그보다 훨씬 먼저(며칠 전부터) 채워져
// 있는 걸 진단 엔드포인트로 실측 확인했습니다(예: 2026-09-24 경기를 2026-09-23에 조회해도
// 채워져 있었음).
//
// 이 gameId는 우리 내부에서 쓰는 13자리 KBO 스타일 ID(예: "20260924NCKT0")이지만, 네이버
// game-polling은 현재 시즌 경기에 한해 그 뒤에 4자리 연도를 한 번 더 붙인 폼(예:
// "20260924NCKT02026")을 요구합니다(FullGameCollector.TryAlternateGameId와 같은 규칙). 과거
// 시즌 아카이브 경기는 반대로 순수 13자리 폼을 씁니다. 어느 쪽인지 미리 알 수 없으므로 순수
// 폼을 먼저 시도하고 실패하면 연도를 붙여 한 번 더 시도합니다.
public static class ProbableStarterFetcher
{
    public sealed record ProbableStarters(string? HomeStarterName, string? AwayStarterName);

    public static async Task<ProbableStarters?> TryFetchAsync(HttpClient http, string gameId, CancellationToken ct = default)
    {
        if (gameId.Length < 4) return null;
        return await TryUrlAsync(http, gameId, ct).ConfigureAwait(false)
            ?? await TryUrlAsync(http, gameId + gameId[..4], ct).ConfigureAwait(false);
    }

    private static async Task<ProbableStarters?> TryUrlAsync(HttpClient http, string naverGameId, CancellationToken ct)
    {
        var url = $"https://api-gw.sports.naver.com/schedule/games/{Uri.EscapeDataString(naverGameId)}/game-polling?isHighlight=false";
        string json;
        try
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return null; }

        var game = root?["result"]?["game"];
        if (game is null) return null;

        string? Read(string field)
        {
            var value = game[field]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        var home = Read("homeStarterName");
        var away = Read("awayStarterName");
        return home is null && away is null ? null : new ProbableStarters(home, away);
    }
}
