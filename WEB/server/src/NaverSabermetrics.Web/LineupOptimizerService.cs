using System.Collections.Concurrent;
using System.Globalization;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;

namespace NaverSabermetrics.Web;

/// <summary>
/// 라인업 최적화 페이지의 오케스트레이션 서비스. 두 가지 알고리즘을 함께 제공합니다.
///
/// 1) 알고리즘1(휴리스틱): 오늘 상대 선발투수의 던지는 손에 맞춘 wOBA로 9명을 순위 매긴 뒤,
///    "2번에 최고 타자를 놓는" 고전적인 세이버메트릭 타순 패턴(순위→타순: 1→2번, 2→1번,
///    3~9→3~9번)으로 즉시 배치합니다. 계산이 가벼워 요청마다 바로 돌립니다.
/// 2) 알고리즘2(전수조사): BattingOrderSimulator.FindBestOrder로 9!=362,880가지 타순을 모두
///    평가합니다. 한 번에 약 48~49초가 걸리므로 HTTP 요청 안에서 동기로 절대 돌리지 않고,
///    (날짜, 팀, 타자 9명의 pcode 집합, 상대 선발투수 손) 조합을 키로 캐싱하면서 백그라운드
///    Task로 실행합니다. 첫 요청은 "computing" 상태로 즉시 응답하고, 같은 조합의 이후 요청은
///    같은 백그라운드 Task를 공유(중복 계산 방지)하다가 완료되면 결과를 돌려줍니다. 이 서비스는
///    싱글턴으로 등록되어야 이 캐시가 요청 간에 유지됩니다.
/// </summary>
public sealed class LineupOptimizerService
{
    private readonly DatabaseCacheService db;
    private readonly QueryGate gate;
    private readonly ILogger<LineupOptimizerService> logger;

    // 알고리즘2(전수조사) 결과/진행 캐시. 값은 배경에서 도는 Task 그 자체 — 완료 전 요청은
    // 이 Task를 공유하고(중복 계산 방지), 완료 후 요청은 즉시 Task.Result를 읽습니다. DB(영구
    // 저장)가 진실의 원천이고, 이 인메모리 캐시는 같은 프로세스 안에서 동시에 들어온 요청들이
    // 전수조사를 중복으로 돌리지 않게 막아 주는 역할만 합니다.
    private readonly ConcurrentDictionary<string, Task<(int[] Order, double ExpectedRuns)>> algorithm2Cache = new();

    // 같은 조합을 여러 요청이 완료 직후 동시에 읽더라도 DB에는 한 번만 쓰도록 막는 가드입니다
    // (INSERT OR REPLACE라 중복 실행 자체는 무해하지만, 불필요한 쓰기를 피합니다).
    private readonly ConcurrentDictionary<string, byte> algorithm2Persisted = new();

    // 시즌별 모델 보정계수(실제 KBO 평균 득점 / 리그 평균 타자 9명 가정 모델 기대 득점) 캐시.
    // 시즌 데이터가 크게 바뀌지 않는 한 다시 계산할 필요가 없어 프로세스 생애주기 동안 재사용합니다.
    private readonly ConcurrentDictionary<int, Task<double>> calibrationFactorCache = new();

    private const int MinHandSplitSamplePa = 30; // (표시용) 상대손 스플릿을 "충분한 표본"으로 볼 최소 타석 합계
    private const int MinPitcherSplitOuts = 60; // 20이닝 — 상대 선발투수의 좌/우 스플릿 FIP를 신뢰하는 최소 아웃수
    private const int RosterLookbackDays = 14; // 그날 등록 현황 스냅샷이 없을 때 거슬러 올라가 찾는 최대 일수
    private const int RecentFormLookbackDays = 30; // "최근 전적"으로 보는 기간
    private const double HandShrinkageK = 40.0; // 상대손 스플릿을 전체 기록과 섞을 때 쓰는 셰인크리지 상수(타석).
                                                 // credibility = handPA / (handPA + K) — 표본이 클수록 스플릿 값에 가까워지고, 작을수록 전체 기록으로 수렴합니다.
    private const double RecentFormWeightMultiplier = 2.5; // 최근 30일 표본 1타석을 시즌 전체 표본 대비 몇 배로 쳐줄지
                                                             // (현재 컨디션이 더 중요하다고 보되, 표본이 큰 쪽이 자연히 우세해지는 가중평균이라 극단적으로 치우치지 않습니다.)
    // 조합 탐색 전, 포지션 카테고리별로 adjustedWoba 상위 몇 명까지 후보로 남길지(포지션 조건을
    // 항상 만족시키기 위한 최소 보장 + 약간의 여유분). 합쳐도 15명을 넘지 않아 C(15,9)=5,005개
    // 조합 정도로, 요청마다 동기로 돌려도 충분히 가볍습니다(포지션 조건을 만족 못 하는 조합은
    // 평가 자체를 건너뜁니다).
    private const int CatcherShortlistSize = 3;
    private const int InfieldShortlistSize = 5;
    private const int OutfieldShortlistSize = 5;
    private const int WildcardShortlistSize = 2; // 카테고리 무관 전체 상위 몇 명(지명타자 전용 등, 위 카테고리에 안 잡히는 고wOBA 후보 보정용)

    private const double QualifyingPaGamesMultiplier = 3.1; // KBO 규정타석 = 팀 경기수 × 3.1
    private const double StarterBonusFactor = 1.08; // 규정타석의 50% 이상을 채운 주전에게 주는 상성지표 보너스 배수(8%)

    public LineupOptimizerService(DatabaseCacheService db, QueryGate gate, ILogger<LineupOptimizerService> logger)
    {
        this.db = db;
        this.gate = gate;
        this.logger = logger;
    }

    public async Task<object> GetOptimalLineupAsync(string teamCode, string gameDate, CancellationToken ct)
    {
        teamCode = teamCode.Trim().ToUpperInvariant();

        var entries = await gate.RunAsync(t => db.GetDailyLineupEntriesAsync(gameDate, t), ct);
        var ownEntries = entries.Where(e => string.Equals(e.TeamCode, teamCode, StringComparison.OrdinalIgnoreCase)).ToList();
        var opponentCode = ownEntries.Select(e => e.OpponentTeamCode).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        // 포지션 해석: 가장 최근 실제 출전 때 라인업 패널에 찍힌 구체적 포지션을 먼저 찾고(specific),
        // 못 찾으면 OfficialPlayerProfiles의 광범위 카테고리로 폴백합니다(specific=false).
        async Task<Dictionary<string, (string? Position, bool Specific)>> ResolvePositionsAsync(IReadOnlyCollection<string> ps)
        {
            var resolved = new Dictionary<string, (string?, bool)>();
            if (ps.Count == 0) return resolved;
            var specific = await gate.RunAsync(t => db.GetMostRecentSpecificPositionsAsync(ps, gameDate, 60, t), ct);
            var missing = ps.Where(p => !specific.ContainsKey(p)).ToList();
            var broad = missing.Count > 0 ? await gate.RunAsync(t => db.GetOfficialPositionMapAsync(missing, t), ct) : new Dictionary<string, string?>();
            foreach (var p in ps)
            {
                if (specific.TryGetValue(p, out var sp)) resolved[p] = (sp, true);
                else if (broad.TryGetValue(p, out var bp)) resolved[p] = (bp, false);
                else resolved[p] = (null, false);
            }
            return resolved;
        }

        // 후보군: 그날 공식/예고 라인업이 아니라, KBO 공식 "전체 등록 현황"(그 팀에 등록된 타자
        // 전원)을 씁니다 — 실제로 어제/최근에 나갔는지는 안 보고, "지금 등록돼 있어서 나갈 수 있는
        // 선수"면 전부 후보입니다. 이 후보군 안에서 각자의 wOBA(최근 전적 + 상대손 스플릿, 그리고
        // 아래 상대 선발투수 FIP 보정까지 반영)를 계산해, 상대 투수와 가장 상성이 좋은 9명을 그
        // 값으로 직접 뽑습니다(순서만 최적화하는 게 아니라 "누구를 낼지"부터 이 기준으로 정합니다).
        var rosterCandidates = await gate.RunAsync(t => db.GetLatestEntryRosterBattersAsync(teamCode, gameDate, RosterLookbackDays, t), ct);
        if (rosterCandidates.Count < 9)
        {
            return new
            {
                available = false,
                team = teamCode,
                gameDate,
                reason = "등록 로스터에서 타자 9명을 찾지 못했습니다(KBO 공식 등록 현황이 아직 이 날짜로 수집되지 않았을 수 있습니다).",
            };
        }

        // 상대 선발투수와 던지는 손.
        string? opponentPitcherName = null, opponentPitcherPcode = null, opponentHand = null;
        if (!string.IsNullOrWhiteSpace(opponentCode))
        {
            var oppPitcher = entries.FirstOrDefault(e =>
                string.Equals(e.TeamCode, opponentCode, StringComparison.OrdinalIgnoreCase) && e.Role == "pitcher");
            opponentPitcherName = oppPitcher?.PlayerName;
            opponentPitcherPcode = oppPitcher?.Pcode;
            if (!string.IsNullOrWhiteSpace(opponentPitcherPcode))
            {
                var handMap = await gate.RunAsync(t => db.GetBatsThrowsMapAsync(new[] { opponentPitcherPcode }, t), ct);
                if (handMap.TryGetValue(opponentPitcherPcode, out var batsThrows))
                    opponentHand = DatabaseCacheService.ParseBatsThrows(batsThrows).Throws;
            }
        }

        // 상대 선발투수의 좌/우 타자 상대 FIP 스플릿. (날짜/팀과 무관하게 그 투수 pcode 하나로
        // 통산 정규시즌 기록을 집계하므로) 계산이 실패해도(표본 부족 등) 보정 없이 계속 진행합니다.
        PitcherFipSplits? pitcherFipSplits = null;
        if (!string.IsNullOrWhiteSpace(opponentPitcherPcode))
        {
            try { pitcherFipSplits = await gate.RunAsync(t => db.GetPitcherFipSplitAsync(opponentPitcherPcode, t), ct); }
            catch (Exception ex) { logger.LogWarning(ex, "상대 선발투수 FIP 스플릿 계산 실패: {Pcode}", opponentPitcherPcode); }
        }

        var candidatePcodes = rosterCandidates.Select(r => r.Pcode).Distinct().ToList();
        var seasonSplits = await gate.RunAsync(t => db.GetLineupOptimizerHandSplitsAsync(candidatePcodes, t), ct);
        var recentSince = DateTime.ParseExact(gameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .AddDays(-RecentFormLookbackDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recentSplits = await gate.RunAsync(t => db.GetLineupOptimizerRecentHandSplitsAsync(candidatePcodes, recentSince, gameDate, t), ct);
        var league = await gate.RunAsync(t => db.GetLeagueReferenceAsync(cancellationToken: t), ct);
        var candidateBatsThrows = await gate.RunAsync(t => db.GetBatsThrowsMapAsync(candidatePcodes, t), ct);
        var seasonYear = int.TryParse(gameDate.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) ? y : (int?)null;
        var wobaConstants = league.GetWobaConstants(seasonYear);

        double WobaFor(LineupOptimizerBatterCounts c) => c.PA > 0
            ? wobaConstants.Calculate(c.AB, c.Walks, intentionalWalks: 0, c.HitByPitch, sacrificeFlies: 0,
                c.Singles, c.Doubles, c.Triples, c.HomeRuns) ?? 0.0
            : 0.0;
        BattingOrderSimulator.BatterProbabilities ProbFor(LineupOptimizerBatterCounts c) =>
            BattingOrderSimulator.BatterProbabilities.FromCounts(c.PA, c.Walks, c.HitByPitch, c.Singles, c.Doubles, c.Triples, c.HomeRuns);

        // 두 확률분포를 표본 가중치(wa, wb)로 섞습니다. 각 성분(볼넷/1루타/…/아웃)을 개별적으로
        // 가중평균하므로 결과도 항상 합이 1인 유효한 확률분포입니다.
        static BattingOrderSimulator.BatterProbabilities MixProb(
            BattingOrderSimulator.BatterProbabilities a, double wa, BattingOrderSimulator.BatterProbabilities b, double wb)
        {
            var total = wa + wb;
            if (total <= 0) return new(1, 0, 0, 0, 0, 0);
            double M(double x, double y) => (x * wa + y * wb) / total;
            return new(M(a.Out, b.Out), M(a.Walk, b.Walk), M(a.Single, b.Single), M(a.Double, b.Double), M(a.Triple, b.Triple), M(a.HomeRun, b.HomeRun));
        }

        // 한 기간(시즌 전체 또는 최근 30일)에서 "상대손 스플릿"과 "전체 기록"을, 상대손 스플릿의
        // 표본 크기에 비례한 신뢰도(셰인크리지)로 매끄럽게 섞습니다 — 표본이 적으면 경성 임계값으로
        // 뚝 끊어 전체 기록에 의존하던 이전 방식 대신, 표본이 커질수록 스플릿 값에 서서히 수렴합니다.
        (BattingOrderSimulator.BatterProbabilities Prob, double Woba) BlendHandIntoOverall(
            LineupOptimizerBatterCounts overall, LineupOptimizerBatterCounts hand)
        {
            var overallProb = ProbFor(overall);
            var overallWoba = WobaFor(overall);
            if (opponentHand is null || hand.PA <= 0) return (overallProb, overallWoba);
            var handProb = ProbFor(hand);
            var handWoba = WobaFor(hand);
            var credibility = hand.PA / (hand.PA + HandShrinkageK);
            var blendedProb = MixProb(handProb, credibility, overallProb, 1 - credibility);
            var blendedWoba = handWoba * credibility + overallWoba * (1 - credibility);
            return (blendedProb, blendedWoba);
        }

        // 최종적으로 그 타자에게 쓸 확률분포/wOBA — "시즌 전체(+상대손 스플릿)"과 "최근 30일(+상대손
        // 스플릿)"을, 각자의 표본 크기(타석수, 최근 기록은 RecentFormWeightMultiplier배로 가중)로
        // 가중평균합니다. 표본이 큰 쪽(주전 선수의 시즌 전체 기록 등)이 자연히 더 크게 반영되고,
        // 동시에 최근 컨디션에는 타석당 더 큰 비중을 줍니다 — 경성 tier 대신 연속적인 신뢰도 혼합입니다.
        (double Woba, BattingOrderSimulator.BatterProbabilities Probabilities, int SamplePa, int SeasonPa, bool UsedHandSplit, bool UsedRecentForm) BlendCounts(string pcode)
        {
            var season = seasonSplits.TryGetValue(pcode, out var s) ? s : new LineupOptimizerHandSplits(new(), new(), new());
            var recent = recentSplits.TryGetValue(pcode, out var r) ? r : new LineupOptimizerHandSplits(new(), new(), new());
            var seasonHand = opponentHand == "L" ? season.VsLeft : opponentHand == "R" ? season.VsRight : new LineupOptimizerBatterCounts();
            var recentHand = opponentHand == "L" ? recent.VsLeft : opponentHand == "R" ? recent.VsRight : new LineupOptimizerBatterCounts();

            var (seasonProb, seasonWoba) = BlendHandIntoOverall(season.Overall, seasonHand);
            var (recentProb, recentWoba) = BlendHandIntoOverall(recent.Overall, recentHand);

            var seasonWeight = (double)season.Overall.PA;
            var recentWeight = recent.Overall.PA * RecentFormWeightMultiplier;
            var totalWeight = seasonWeight + recentWeight;

            var finalProb = totalWeight > 0 ? MixProb(recentProb, recentWeight, seasonProb, seasonWeight) : new(1, 0, 0, 0, 0, 0);
            var finalWoba = totalWeight > 0 ? (recentWoba * recentWeight + seasonWoba * seasonWeight) / totalWeight : 0.0;
            var samplePa = season.Overall.PA + recent.Overall.PA;
            var usedHandSplit = (seasonHand.PA + recentHand.PA) >= MinHandSplitSamplePa;
            var usedRecentForm = recent.Overall.PA > 0;

            return (finalWoba, finalProb, samplePa, season.Overall.PA, usedHandSplit, usedRecentForm);
        }

        // "규정타석의 50% 이상을 채운 주전"에게는 (실력이 같아 보여도) 표본이 훨씬 크고 안정적이므로
        // 우선권을 줍니다 — 표본이 작아 우연히 wOBA가 튄 벤치 선수가 더 높은 순위로 잡히는 걸
        // 막기 위함입니다. 규정타석은 시즌 내내 고정값이 아니라 "그 팀이 그 시즌 치른 경기 수 ×
        // 3.1"로 매일 올라가므로, 오늘 이 팀이 치른 경기 수 기준으로 그때그때 계산합니다.
        var teamGamesPlayed = await gate.RunAsync(t => db.GetTeamGamesPlayedAsync(teamCode, gameDate, t), ct);
        var regulationPaHalf = teamGamesPlayed * QualifyingPaGamesMultiplier * 0.5;

        // 상대 선발투수의 "좌/우 스플릿 FIP ÷ 그 투수의 통산 FIP" 비율을, 타자 본인의 타석 방향에
        // 맞춰 돌려줍니다. 표본이 부족하거나(20이닝 미만) 계산 자체가 안 되면 1.0(보정 없음)입니다.
        // 두 FIP 모두 같은(근사) 방식으로 계산하므로 아웃수 근사 오차는 비율에서 상당 부분 상쇄됩니다.
        double GetPitcherPlatoonFactor(string? batterStandSide)
        {
            if (pitcherFipSplits is null) return 1.0;
            var overallFip = pitcherFipSplits.Overall.Fip(league.FipConstant);
            if (overallFip is null || overallFip <= 0) return 1.0;

            var split = batterStandSide == "L" && pitcherFipSplits.VsLeft.Outs >= MinPitcherSplitOuts ? pitcherFipSplits.VsLeft
                : batterStandSide == "R" && pitcherFipSplits.VsRight.Outs >= MinPitcherSplitOuts ? pitcherFipSplits.VsRight
                : null;
            if (split is null) return 1.0;
            var splitFip = split.Fip(league.FipConstant);
            if (splitFip is null || splitFip <= 0) return 1.0;

            return Math.Clamp(splitFip.Value / overallFip.Value, 0.7, 1.4);
        }

        // 소표본으로 계산된 배수가 비확률(음수 아웃 등)을 만들지 않도록 안전하게 적용합니다.
        static BattingOrderSimulator.BatterProbabilities ApplyPitcherFactor(BattingOrderSimulator.BatterProbabilities p, double factor)
        {
            if (Math.Abs(factor - 1.0) < 1e-9) return p;
            var walk = p.Walk * factor;
            var single = p.Single * factor;
            var dbl = p.Double * factor;
            var triple = p.Triple * factor;
            var hr = p.HomeRun * factor;
            var sum = walk + single + dbl + triple + hr;
            if (sum > 0.97)
            {
                var scale = 0.97 / sum;
                walk *= scale; single *= scale; dbl *= scale; triple *= scale; hr *= scale;
                sum = 0.97;
            }
            return new BattingOrderSimulator.BatterProbabilities(1.0 - sum, walk, single, dbl, triple, hr);
        }

        // 등록된 후보 전원(보통 14~20명)에 대해 상성 지표(adjustedWoba)를 계산합니다. 조합 탐색에서
        // "실제로 수비가 가능한 조합인지"(포수 1명 이상, 내야수 4명 이상, 외야수 3명 이상 — 나머지
        // 1명은 지명타자로 채움)를 걸러내야 하므로, 포지션도 여기서 후보 전원에 대해 미리 붙입니다.
        var candidateViews = new List<BatterView>(rosterCandidates.Count);
        foreach (var (pcode, name) in rosterCandidates)
        {
            var (woba, rawProbabilities, samplePa, seasonPa, usedHandSplit, usedRecentForm) = BlendCounts(pcode);

            var (_, batterStand) = DatabaseCacheService.ParseBatsThrows(candidateBatsThrows.TryGetValue(pcode, out var bt) ? bt : null);
            var pitcherFactor = GetPitcherPlatoonFactor(batterStand);
            var isEstablishedStarter = regulationPaHalf > 0 && seasonPa >= regulationPaHalf;
            var combinedFactor = pitcherFactor * (isEstablishedStarter ? StarterBonusFactor : 1.0);
            var probabilities = ApplyPitcherFactor(rawProbabilities, combinedFactor);
            var adjustedWoba = woba * combinedFactor;

            candidateViews.Add(new BatterView(pcode, name, null, false, woba, adjustedWoba, pitcherFactor, samplePa, usedHandSplit, usedRecentForm, isEstablishedStarter, probabilities));
        }

        var allPositions = await ResolvePositionsAsync(candidateViews.Select(b => b.Pcode).ToList());
        candidateViews = candidateViews
            .Select(b => b with { Position = allPositions[b.Pcode].Position, PositionIsSpecific = allPositions[b.Pcode].Specific })
            .ToList();

        // 포지션을 "포수/내야수/외야수/기타(지명타자 전용 등)"로 광범위하게 분류합니다. KBO는
        // 지명타자 제도를 쓰므로 9명의 타자가 (포수1 + 내야수4 + 외야수3 + 지명타자1)을 채워야
        // 실제로 수비 가능한 라인업이 됩니다 — 지명타자 자리는 아무 포지션 선수나 채울 수 있어
        // 카테고리 제약이 없습니다.
        static string PositionCategory(string? position)
        {
            if (string.IsNullOrWhiteSpace(position)) return "UNK";
            if (position.Contains("포수")) return "C";
            if (position.Contains("내야") || position is "1루수" or "2루수" or "3루수" or "유격수") return "IF";
            if (position.Contains("외야") || position is "좌익수" or "중견수" or "우익수") return "OF";
            return "UNK";
        }

        static bool IsPositionFeasible(IEnumerable<BatterView> combo)
        {
            var categories = combo.Select(b => PositionCategory(b.Position)).ToList();
            return categories.Count(c => c == "C") >= 1
                && categories.Count(c => c == "IF") >= 4
                && categories.Count(c => c == "OF") >= 3;
        }

        var totalCatchers = candidateViews.Count(b => PositionCategory(b.Position) == "C");
        var totalInfielders = candidateViews.Count(b => PositionCategory(b.Position) == "IF");
        var totalOutfielders = candidateViews.Count(b => PositionCategory(b.Position) == "OF");
        if (totalCatchers < 1 || totalInfielders < 4 || totalOutfielders < 3)
        {
            return new
            {
                available = false,
                team = teamCode,
                gameDate,
                reason = "등록 로스터의 포지션 정보로는 실제 수비가 가능한 9명(포수 1·내야수 4·외야수 3 이상)을 구성할 수 없습니다.",
            };
        }

        // 9명을 뽑을 때도 "개별 adjustedWoba 합이 가장 높은 9명"이 실제로 최선의 조합이라는 보장은
        // 없고(타순 내 상호작용 효과), 게다가 포지션까지 지켜야 하므로 순수 전체 상위 K명으로만
        // 추리면 특정 포지션(주로 포수)이 아예 후보에서 빠질 수 있습니다. 그래서 카테고리별로 상위
        // 몇 명씩을 보장해 후보를 추리고(포수/내야수/외야수 각각, + 카테고리 무관 전체 상위 몇 명),
        // 그 후보들로 만들 수 있는 모든 9명 조합 중 포지션 조건을 만족하는 것만 걸러 "2번에 최고
        // 타자" 고전 패턴으로 배치해 기대 득점을 1회씩 평가하고, 가장 높은 조합+순서를 최종
        // 선택으로 씁니다. 이 승자의 순서가 그대로 알고리즘1(휴리스틱)의 답이 되고, 이후
        // 알고리즘2(전수조사)는 이 조합 하나에 대해서만 9!을 모두 살펴봅니다.
        var shortlistMap = new Dictionary<string, BatterView>();
        void AddTopByCategory(string category, int take)
        {
            foreach (var b in candidateViews.Where(b => PositionCategory(b.Position) == category)
                         .OrderByDescending(b => b.AdjustedWoba).Take(take))
                shortlistMap[b.Pcode] = b;
        }
        AddTopByCategory("C", CatcherShortlistSize);
        AddTopByCategory("IF", InfieldShortlistSize);
        AddTopByCategory("OF", OutfieldShortlistSize);
        foreach (var b in candidateViews.OrderByDescending(x => x.AdjustedWoba).Take(WildcardShortlistSize))
            shortlistMap[b.Pcode] = b;
        var shortlist = shortlistMap.Values.ToList();
        if (shortlist.Count < 9)
        {
            return new
            {
                available = false,
                team = teamCode,
                gameDate,
                reason = "등록 로스터에서 타자 9명을 찾지 못했습니다.",
            };
        }

        static IEnumerable<List<T>> NineCombinations<T>(IReadOnlyList<T> items)
        {
            const int k = 9;
            var n = items.Count;
            if (n < k) yield break;
            var idx = new int[k];
            for (var i = 0; i < k; i++) idx[i] = i;
            while (true)
            {
                yield return idx.Select(i => items[i]).ToList();
                var pos = k - 1;
                while (pos >= 0 && idx[pos] == n - k + pos) pos--;
                if (pos < 0) yield break;
                idx[pos]++;
                for (var i = pos + 1; i < k; i++) idx[i] = idx[pos] + (i - pos);
            }
        }

        List<BatterView>? bestCombo = null;
        BatterView[]? bestOrder = null;
        var bestExpectedRuns = double.NegativeInfinity;
        foreach (var combo in NineCombinations(shortlist))
        {
            if (!IsPositionFeasible(combo)) continue;

            var rankedCombo = combo.OrderByDescending(b => b.AdjustedWoba).ToList();
            var order = new BatterView[9];
            order[0] = rankedCombo[1];
            order[1] = rankedCombo[0];
            for (var i = 2; i < 9; i++) order[i] = rankedCombo[i];

            double runs;
            try { runs = BattingOrderSimulator.ExpectedRuns(order.Select(b => b.Probabilities).ToList()); }
            catch (Exception ex) { logger.LogWarning(ex, "라인업 조합 평가 실패"); continue; }

            if (runs > bestExpectedRuns)
            {
                bestExpectedRuns = runs;
                bestCombo = combo;
                bestOrder = order;
            }
        }
        if (bestCombo is null || bestOrder is null)
        {
            return new
            {
                available = false,
                team = teamCode,
                gameDate,
                reason = "포지션 조건(포수 1·내야수 4·외야수 3 이상)을 만족하는 라인업 조합을 찾지 못했습니다.",
            };
        }

        var batterViews = bestCombo;
        var pcodes = batterViews.Select(b => b.Pcode).ToList();
        const string poolSource = "roster_combo_best9";

        // 알고리즘1(휴리스틱)의 답 = 위 조합 탐색에서 이긴 조합의 순서.
        var byPcodeForOrder = batterViews.ToDictionary(b => b.Pcode, b => b);
        var algorithm1Slots = bestOrder.Select(b => byPcodeForOrder[b.Pcode]).ToArray();

        // 모델 보정계수: "실제 KBO 평균 득점 / 리그 평균 타자 9명 가정 모델 기대 득점"(시즌 단위,
        // 계산 후 캐시). 계산 자체가 실패해도(과거 시즌 데이터 부족 등) 보정 없이(1.0) 계속 진행합니다.
        double? calibrationFactor = null;
        try { calibrationFactor = await GetCalibrationFactorAsync(seasonYear ?? DateTime.UtcNow.Year, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "라인업 최적화 보정계수 계산 실패(시즌 {Year})", seasonYear); }

        // 알고리즘1도 같은 시뮬레이터로 한 번만 평가하면(전수조사가 아니라 이 순서 하나만) 가벼우므로,
        // 알고리즘2와 apples-to-apples 비교가 되도록 기대 득점/환산 예상 득점을 함께 계산합니다.
        double? algorithm1ExpectedRuns = null, algorithm1EstimatedRuns = null;
        try
        {
            algorithm1ExpectedRuns = BattingOrderSimulator.ExpectedRuns(algorithm1Slots.Select(b => b.Probabilities).ToList());
            if (calibrationFactor.HasValue) algorithm1EstimatedRuns = algorithm1ExpectedRuns * calibrationFactor.Value;
        }
        catch (Exception ex) { logger.LogWarning(ex, "알고리즘1 기대 득점 계산 실패"); }

        // 알고리즘2: (날짜, 팀, 9명 pcode 집합, 상대손) 키. 먼저 DB 영구 캐시를 확인해, 이미 계산해
        // 저장해 둔 결과가 있으면(앱 재시작 후에도) 전수조사를 다시 돌리지 않고 바로 돌려줍니다.
        var sortedPcodeKey = string.Join(",", pcodes.OrderBy(p => p, StringComparer.Ordinal));
        // 상대 선발투수 pcode도 키에 포함합니다 — 이제 wOBA/확률이 그 투수의 좌우 스플릿 FIP로
        // 보정되므로, 같은 날짜/팀/타자9명/상대손이어도 선발투수가 바뀌면 다시 계산해야 합니다.
        var cacheKey = $"{gameDate}|{teamCode}|{sortedPcodeKey}|{opponentHand ?? "-"}|{opponentPitcherPcode ?? "-"}";
        var probabilitiesInOrder = batterViews.Select(b => b.Probabilities).ToList();
        var byPcode = batterViews.ToDictionary(b => b.Pcode, b => b);

        object algorithm2;
        var stored = await gate.RunAsync(t => db.GetLineupOptimizerResultAsync(cacheKey, t), ct);
        if (stored is not null)
        {
            var storedViews = stored.Algorithm2OrderPcodes
                .Select(pc => byPcode.TryGetValue(pc, out var v) ? v : null)
                .Where(v => v is not null).Select(v => v!).ToArray();
            algorithm2 = storedViews.Length == 9
                ? new
                {
                    status = "ready",
                    order = storedViews.Select(b => b.Name).ToArray(),
                    orderPcodes = storedViews.Select(b => b.Pcode).ToArray(),
                    expectedRuns = Math.Round(stored.Algorithm2ExpectedRuns, 3),
                    estimatedRuns = stored.Algorithm2EstimatedRuns.HasValue ? Math.Round(stored.Algorithm2EstimatedRuns.Value, 3) : (double?)null,
                    calibrationFactor = stored.CalibrationFactor,
                    note = "9명의 서로 다른 타순끼리 비교하기 위한 단순화 모델의 상대적 기대 득점입니다 — estimatedRuns는 리그 평균 득점 대비 선형 보정을 적용한 대략적인 실제 스케일 추정치로, 정밀한 예측이 아닙니다.",
                }
                : null!; // 타자 구성이 바뀌었으면(저장된 pcode를 지금 풀에서 못 찾음) 폴백해서 다시 계산합니다.
        }
        else algorithm2 = null!;

        if (algorithm2 is null)
        {
            var backgroundTask = algorithm2Cache.GetOrAdd(cacheKey,
                _ => Task.Run(() => BattingOrderSimulator.FindBestOrder(probabilitiesInOrder), CancellationToken.None));

            if (backgroundTask.IsFaulted)
            {
                algorithm2Cache.TryRemove(cacheKey, out _);
                logger.LogError(backgroundTask.Exception, "라인업 최적화 전수조사 실패: {Key}", cacheKey);
                algorithm2 = new { status = "error" };
            }
            else if (backgroundTask.IsCompletedSuccessfully)
            {
                var (order, expectedRuns) = backgroundTask.Result;
                double? estimatedRuns = calibrationFactor.HasValue ? expectedRuns * calibrationFactor.Value : null;
                var orderPcodes = order.Select(i => batterViews[i].Pcode).ToArray();

                if (algorithm2Persisted.TryAdd(cacheKey, 0))
                {
                    try
                    {
                        await gate.RunAsync(async t =>
                        {
                            await db.SaveLineupOptimizerResultAsync(
                                cacheKey, gameDate, teamCode, opponentCode, opponentHand, pcodes,
                                algorithm1Slots.Select(b => b.Pcode).ToArray(), algorithm1ExpectedRuns, algorithm1EstimatedRuns,
                                orderPcodes, expectedRuns, estimatedRuns, calibrationFactor, t).ConfigureAwait(false);
                            return true;
                        }, ct);
                    }
                    catch (Exception ex)
                    {
                        algorithm2Persisted.TryRemove(cacheKey, out _);
                        logger.LogWarning(ex, "라인업 최적화 결과 DB 저장 실패: {Key}", cacheKey);
                    }
                }

                algorithm2 = new
                {
                    status = "ready",
                    order = order.Select(i => batterViews[i].Name).ToArray(),
                    orderPcodes,
                    expectedRuns = Math.Round(expectedRuns, 3),
                    estimatedRuns = estimatedRuns.HasValue ? Math.Round(estimatedRuns.Value, 3) : (double?)null,
                    calibrationFactor,
                    note = "9명의 서로 다른 타순끼리 비교하기 위한 단순화 모델의 상대적 기대 득점입니다 — estimatedRuns는 리그 평균 득점 대비 선형 보정을 적용한 대략적인 실제 스케일 추정치로, 정밀한 예측이 아닙니다.",
                };
            }
            else
            {
                algorithm2 = new { status = "computing" };
            }
        }

        double? pitcherFipOverall = null, pitcherFipVsLeft = null, pitcherFipVsRight = null;
        if (pitcherFipSplits is not null)
        {
            pitcherFipOverall = pitcherFipSplits.Overall.Fip(league.FipConstant);
            if (pitcherFipSplits.VsLeft.Outs >= MinPitcherSplitOuts) pitcherFipVsLeft = pitcherFipSplits.VsLeft.Fip(league.FipConstant);
            if (pitcherFipSplits.VsRight.Outs >= MinPitcherSplitOuts) pitcherFipVsRight = pitcherFipSplits.VsRight.Fip(league.FipConstant);
        }

        return new
        {
            available = true,
            team = teamCode,
            gameDate,
            opponent = new
            {
                team = opponentCode,
                pitcher = opponentPitcherName,
                pitcherHand = opponentHand,
                pitcherFip = pitcherFipOverall.HasValue ? Math.Round(pitcherFipOverall.Value, 2) : (double?)null,
                pitcherFipVsLeft = pitcherFipVsLeft.HasValue ? Math.Round(pitcherFipVsLeft.Value, 2) : (double?)null,
                pitcherFipVsRight = pitcherFipVsRight.HasValue ? Math.Round(pitcherFipVsRight.Value, 2) : (double?)null,
            },
            batterPoolSource = poolSource,
            batters = batterViews.Select(b => new
            {
                b.Pcode,
                b.Name,
                position = b.Position,
                positionIsSpecific = b.PositionIsSpecific,
                woba = Math.Round(b.Woba, 3),
                adjustedWoba = Math.Round(b.AdjustedWoba, 3),
                pitcherFactor = Math.Round(b.PitcherFactor, 3),
                samplePa = b.SamplePa,
                usedHandSplit = b.UsedHandSplit,
                usedRecentForm = b.UsedRecentForm,
                isEstablishedStarter = b.IsEstablishedStarter,
            }),
            algorithm1 = new
            {
                description = "등록 로스터 중 상대 선발투수와 상성(시즌+최근 30일 wOBA 가중 블렌드, 상대손 스플릿·상대 선발투수 FIP 스플릿 보정 반영)이 좋은 상위 후보들로 가능한 9명 조합을 모두 평가해, 2번 타순에 최고 타자를 배치하는 고전적인 세이버메트릭 패턴(2-1-3-4-5-6-7-8-9)으로 기대 득점이 가장 높았던 조합을 즉시 배치한 결과입니다.",
                order = algorithm1Slots.Select(b => b.Name).ToArray(),
                orderPcodes = algorithm1Slots.Select(b => b.Pcode).ToArray(),
                expectedRuns = algorithm1ExpectedRuns.HasValue ? Math.Round(algorithm1ExpectedRuns.Value, 3) : (double?)null,
                estimatedRuns = algorithm1EstimatedRuns.HasValue ? Math.Round(algorithm1EstimatedRuns.Value, 3) : (double?)null,
            },
            algorithm2,
        };
    }

    /// <summary>시즌별 모델 보정계수 = 그 시즌 실제 KBO "팀당 경기당 득점" 평균 ÷ 그 시즌 리그 평균
    /// 타자 9명을 가정한 합성 타순의 모델 기대 득점(BattingOrderSimulator.ExpectedRuns 1회 평가).
    /// 시즌 데이터가 부족하면(타석 표본 0 또는 완료된 경기 0) 1.0(보정 없음)을 돌려줍니다.</summary>
    private Task<double> GetCalibrationFactorAsync(int seasonYear, CancellationToken ct)
        => calibrationFactorCache.GetOrAdd(seasonYear, _ => ComputeCalibrationFactorAsync(seasonYear));

    private async Task<double> ComputeCalibrationFactorAsync(int seasonYear)
    {
        var totals = await gate.RunAsync(t => db.GetSeasonLeagueBattingTotalsAsync(seasonYear, t), CancellationToken.None);
        var actualRunsPerTeamPerGame = await gate.RunAsync(t => db.GetSeasonActualRunsPerTeamPerGameAsync(seasonYear, t), CancellationToken.None);
        if (totals.PlateAppearances <= 0 || actualRunsPerTeamPerGame <= 0) return 1.0;

        var averageBatter = BattingOrderSimulator.BatterProbabilities.FromCounts(
            totals.PlateAppearances, totals.Walks, totals.HitByPitch,
            totals.Singles, totals.Doubles, totals.Triples, totals.HomeRuns);
        var syntheticLineup = Enumerable.Repeat(averageBatter, 9).ToList();
        var modelRunsForAverageLineup = BattingOrderSimulator.ExpectedRuns(syntheticLineup);
        if (modelRunsForAverageLineup <= 0) return 1.0;

        return actualRunsPerTeamPerGame / modelRunsForAverageLineup;
    }

    private sealed record BatterView(string Pcode, string Name, string? Position, bool PositionIsSpecific, double Woba, double AdjustedWoba,
        double PitcherFactor, int SamplePa, bool UsedHandSplit, bool UsedRecentForm, bool IsEstablishedStarter, BattingOrderSimulator.BatterProbabilities Probabilities);
}
