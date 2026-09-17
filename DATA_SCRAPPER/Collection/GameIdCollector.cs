using System.Net.Http;
using System.Text.Json;
using NaverRelayUI.Models;

namespace NaverRelayUI.Collection
{
    public static class GameIdCollector
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        // Page size is fixed at 10 server-side — confirmed by testing pageSize=50
        // (no effect) vs page=2 (moved forward 10 items). Don't bother sending pageSize.
        private const int PageSize = 10;

        public static async Task<List<ScheduleGame>> CollectAllKboGamesAsync(
            HttpClient http,
            string fromDate, // "yyyy-MM-dd"
            string toDate,
            int delayMs = 300,
            Action<string>? log = null,
            IProgress<(int done, int total)>? progress = null,
            CancellationToken ct = default)
        {
            var all = new List<ScheduleGame>();
            int page = 1;
            int totalCount = int.MaxValue; // unknown until first response

            while ((page - 1) * PageSize < totalCount)
            {
                ct.ThrowIfCancellationRequested();

                var url = "https://api-gw.sports.naver.com/schedule/games" +
                          $"?upperCategoryId=kbaseball&fromDate={fromDate}&toDate={toDate}&page={page}";

                var json = await http.GetStringAsync(url, ct);
                var parsed = JsonSerializer.Deserialize<ScheduleGamesResponse>(json, JsonOptions);

                if (parsed is null || !parsed.Success || parsed.Code != 200 || parsed.Result == null)
                    throw new InvalidDataException($"네이버 일정 {page}페이지 응답이 올바르지 않습니다.");

                var games = parsed.Result.Games;
                if (games == null || games.Count == 0)
                {
                    if (parsed.Result.GameTotalCount > all.Count)
                        throw new InvalidDataException("네이버 일정 일부 페이지가 비어 있습니다. 다시 실행하세요.");
                    break;
                }

                totalCount = parsed.Result.GameTotalCount;
                all.AddRange(games);

                log?.Invoke($"일정 {page}페이지: +{games.Count}건 (누적 {all.Count}/{totalCount})");
                progress?.Report((all.Count, totalCount));

                page++;
                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }

            // Real KBO league games only — drop scrimmages/youth/other (categoryId != "kbo").
            // NOTE: this listing endpoint has no roundCode field, so All-Star/postseason
            // games (categoryId is still "kbo") slip through here — RelayCollector checks
            // roundCode == "kbo_r" after fetching each game and routes non-regular-season
            // games into the _excluded folder instead.
            return all.Where(g => g.CategoryId == "kbo").ToList();
        }
    }
}
