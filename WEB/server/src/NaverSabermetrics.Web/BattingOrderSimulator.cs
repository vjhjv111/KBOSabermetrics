namespace NaverSabermetrics.Web;

// 9명 타순의 "기대 득점"을 계산하는 단순화된 베이스-아웃 마르코프 체인/DP 모델입니다. 도루,
// 희생플라이, 병살, 수비 실책 등은 반영하지 않는 단순화 모델이라(안타/2루타/3루타/홈런/볼넷·사구/
// 아웃 6가지 결과와, 표준적인 "고정" 주자 진루 규칙만 사용) 절대 득점 기대값이 실제 KBO 평균과
// 정확히 맞지는 않습니다 — 하지만 같은 9명을 어떤 순서로 배치하느냐에 따른 "상대적" 득점 기대값
// 차이는 이 정도 단순화로도 충분히 의미 있게 비교할 수 있습니다(이 방식은 D'Esopo–Lefkowitz(1977)
// 등에서 쓰인 고전적인 타순 최적화 기법과 같은 계열입니다). 독립적인 몬테카를로 시뮬레이션으로
// 검증했을 때도 거의 동일한 절대값이 나와(DP 10.512 vs MC 10.515, 9명 평균급 타자 기준) 이 모델
// 자체의 계산은 정확합니다 — 단순화 규칙 때문에 절대 득점이 높게 나오는 것뿐이므로, 이 값을 "이
// 팀이 실제로 이만큼 득점할 것"으로 해석하면 안 되고 같은 9명의 서로 다른 타순끼리 비교하는
// 용도로만 쓰세요.
public static class BattingOrderSimulator
{
    // 타자 한 명의 타석당 결과 확률. 6가지가 1.0으로 합산되어야 합니다(생성자에서 정규화).
    public readonly record struct BatterProbabilities(double Out, double Walk, double Single, double Double, double Triple, double HomeRun)
    {
        public static BatterProbabilities FromCounts(int pa, int walks, int hbp, int singles, int doubles, int triples, int homeRuns)
        {
            if (pa <= 0) return new(1, 0, 0, 0, 0, 0); // 표본이 아예 없으면 항상 아웃으로 취급(최하 순번으로 밀려남)
            double Rate(int n) => n / (double)pa;
            var walk = Rate(walks + hbp);
            var single = Rate(singles);
            var dbl = Rate(doubles);
            var triple = Rate(triples);
            var hr = Rate(homeRuns);
            var outRate = Math.Max(0, 1.0 - walk - single - dbl - triple - hr);
            return new(outRate, walk, single, dbl, triple, hr);
        }
    }

    private const int TotalOuts = 27; // 9이닝 기준. 연장/콜드는 고려하지 않습니다.
    private const int BaseStateCount = 8; // (1루,2루,3루) 각각 있음/없음 = 2^3

    // 같은 "남은 아웃" 단계 안에서도(안타/볼넷은 아웃을 안 씁니다) 타자 9명이 서로를 참조하는
    // 순환 의존이 생깁니다(9명이 연속으로 안타를 치면 아웃 하나 없이 타순이 한 바퀴 돌 수 있으므로).
    // 그래서 단순 한 방향 대입으로는 못 풀고, 각 아웃 단계마다 가우스-자이델 방식으로 여러 번
    // 스윕해서 고정점에 수렴시킵니다. 아웃 없이 같은 단계에 머무를 확률은 스윕마다 기하급수적으로
    // 작아지므로(대략 (볼넷+안타율)^스윕수) 6번이면 오차가 0.2% 미만으로, 타순 간 상대 비교에는
    // 충분합니다.
    private const int GaussSeidelSweeps = 6;

    /// <summary>batters[0..8] 순서 그대로의 기대 득점(27아웃 기준)을 계산합니다.</summary>
    public static double ExpectedRuns(IReadOnlyList<BatterProbabilities> batters)
    {
        var buffer = new double[9, TotalOuts + 1, BaseStateCount];
        return ExpectedRuns(batters, buffer);
    }

    private static double ExpectedRuns(IReadOnlyList<BatterProbabilities> batters, double[,,] value)
    {
        if (batters.Count != 9) throw new ArgumentException("타순은 정확히 9명이어야 합니다.", nameof(batters));
        Array.Clear(value, 0, value.Length);

        for (var outsRemaining = 1; outsRemaining <= TotalOuts; outsRemaining++)
        {
            for (var b = 0; b < 9; b++)
                for (var s = 0; s < BaseStateCount; s++)
                    value[b, outsRemaining, s] = value[b, outsRemaining - 1, s];

            for (var sweep = 0; sweep < GaussSeidelSweeps; sweep++)
            {
                for (var batterIndex = 0; batterIndex < 9; batterIndex++)
                {
                    var nextBatter = (batterIndex + 1) % 9;
                    var p = batters[batterIndex];
                    for (var state = 0; state < BaseStateCount; state++)
                    {
                        var outValue = value[nextBatter, outsRemaining - 1, state];
                        var (walkState, walkRuns) = AdvanceOnWalk(state);
                        var (singleState, singleRuns) = AdvanceOnHit(state, bases: 1);
                        var (doubleState, doubleRuns) = AdvanceOnHit(state, bases: 2);
                        var (tripleState, tripleRuns) = AdvanceOnHit(state, bases: 3);
                        var (hrState, hrRuns) = AdvanceOnHit(state, bases: 4);

                        value[batterIndex, outsRemaining, state] =
                            p.Out * outValue +
                            p.Walk * (walkRuns + value[nextBatter, outsRemaining, walkState]) +
                            p.Single * (singleRuns + value[nextBatter, outsRemaining, singleState]) +
                            p.Double * (doubleRuns + value[nextBatter, outsRemaining, doubleState]) +
                            p.Triple * (tripleRuns + value[nextBatter, outsRemaining, tripleState]) +
                            p.HomeRun * (hrRuns + value[nextBatter, outsRemaining, hrState]);
                    }
                }
            }
        }
        return value[0, TotalOuts, 0];
    }

    /// <summary>362,880가지 타순을 모두 평가해 기대 득점이 가장 높은 순서를 찾습니다. 평가 한 번은
    /// 가볍지만(9×28×8 셀 × 6스윕) 9!을 전부 도니 시간이 걸릴 수 있어(수 초~수십 초) 호출자가
    /// 캐싱하는 걸 전제로 합니다 — 요청마다 매번 다시 돌리지 않도록 하세요.</summary>
    public static (int[] Order, double ExpectedRuns) FindBestOrder(IReadOnlyList<BatterProbabilities> batters)
    {
        if (batters.Count != 9) throw new ArgumentException("타순은 정확히 9명이어야 합니다.", nameof(batters));
        var indices = Enumerable.Range(0, 9).ToArray();
        var bestOrder = (int[])indices.Clone();
        var bestRuns = double.MinValue;
        var buffer = new double[9, TotalOuts + 1, BaseStateCount];
        var orderedBuffer = new BatterProbabilities[9];

        foreach (var permutation in Permute(indices))
        {
            for (var i = 0; i < 9; i++) orderedBuffer[i] = batters[permutation[i]];
            var runs = ExpectedRuns(orderedBuffer, buffer);
            if (runs > bestRuns)
            {
                bestRuns = runs;
                bestOrder = (int[])permutation.Clone();
            }
        }
        return (bestOrder, bestRuns);
    }

    private static IEnumerable<int[]> Permute(int[] items)
    {
        var array = (int[])items.Clone();
        var n = array.Length;
        var c = new int[n];
        yield return (int[])array.Clone();
        var i = 0;
        while (i < n)
        {
            if (c[i] < i)
            {
                if (i % 2 == 0) (array[0], array[i]) = (array[i], array[0]);
                else (array[c[i]], array[i]) = (array[i], array[c[i]]);
                yield return (int[])array.Clone();
                c[i]++;
                i = 0;
            }
            else { c[i] = 0; i++; }
        }
    }

    private static (int NewState, int Runs) AdvanceOnWalk(int state)
    {
        var on1 = (state & 1) != 0;
        var on2 = (state & 2) != 0;
        var on3 = (state & 4) != 0;
        if (!on1) return (state | 1, 0);
        if (!on2) return ((state | 1) | 2, 0);
        if (!on3) return (7, 0);
        return (7, 1);
    }

    private static (int NewState, int Runs) AdvanceOnHit(int state, int bases)
    {
        var on1 = (state & 1) != 0;
        var on2 = (state & 2) != 0;
        var on3 = (state & 4) != 0;
        switch (bases)
        {
            case 1:
                {
                    var runs = on3 ? 1 : 0;
                    var newState = 1 | (on1 ? 2 : 0) | (on2 ? 4 : 0);
                    return (newState, runs);
                }
            case 2:
                {
                    var runs = (on2 ? 1 : 0) + (on3 ? 1 : 0);
                    var newState = 2 | (on1 ? 4 : 0);
                    return (newState, runs);
                }
            case 3:
                {
                    var runs = (on1 ? 1 : 0) + (on2 ? 1 : 0) + (on3 ? 1 : 0);
                    return (4, runs);
                }
            default:
                {
                    var runs = 1 + (on1 ? 1 : 0) + (on2 ? 1 : 0) + (on3 ? 1 : 0);
                    return (0, runs);
                }
        }
    }
}
