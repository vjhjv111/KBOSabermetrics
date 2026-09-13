using System.ComponentModel;
using System.Reflection;
using System.Globalization;
using NaverRelay.Application.Statistics;

namespace NaverSabermetrics.Web;

public sealed record ViewDefinition(string Role, string Key, string Title, Type RowType);

public static class ViewRegistry
{
    public static readonly IReadOnlyList<ViewDefinition> Views = new ViewDefinition[]
    {
        new("batter", "basic", "기본", typeof(BatterBasicRecordRow)),
        new("batter", "advanced", "심화", typeof(BatterAdvancedRecordRow)),
        new("batter", "value", "가치", typeof(BatterValueRecordRow)),
        new("batter", "extended", "확장", typeof(BatterExtendedRecordRow)),
        new("batter", "clutch", "클러치", typeof(BatterClutchRecordRow)),
        new("batter", "power", "파워", typeof(BatterPowerRecordRow)),
        new("batter", "team-batting", "팀배팅", typeof(BatterTeamBattingRecordRow)),
        new("batter", "steal", "도루", typeof(BatterStealRecordRow)),
        new("batter", "baserunning", "주루", typeof(BatterBaserunningRecordRow)),
        new("batter", "batted-ball", "타구", typeof(BatterBattedBallRecordRow)),
        new("batter", "direction", "타구방향", typeof(BatterDirectionRecordRow)),
        new("batter", "discipline", "투구", typeof(BatterPitchProfileRecordRow)),
        new("batter", "pitch-types", "구종", typeof(BatterPitchTypeMatrixRow)),
        new("pitcher", "basic", "기본", typeof(PitcherBasicRecordRow)),
        new("pitcher", "advanced", "심화", typeof(PitcherAdvancedRecordRow)),
        new("pitcher", "value", "가치", typeof(PitcherDetailedValueRecordRow)),
        new("pitcher", "extended", "확장", typeof(PitcherExtendedRecordRow)),
        new("pitcher", "wp", "WP", typeof(PitcherWinProbabilityRecordRow)),
        new("pitcher", "runner", "주자", typeof(PitcherRunnerRecordRow)),
        new("pitcher", "starter", "선발", typeof(PitcherStarterRecordRow)),
        new("pitcher", "reliever", "구원", typeof(PitcherRelieverRecordRow)),
        new("pitcher", "batted-ball", "타구", typeof(PitcherBattedBallRecordRow)),
        new("pitcher", "direction", "타구방향", typeof(PitcherDirectionRecordRow)),
        new("pitcher", "discipline", "투구", typeof(PitcherPitchProfileRecordRow)),
        new("pitcher", "pitch-types", "구종", typeof(PitcherPitchTypeRecordRow)),
        new("constants", "league", "리그 상수", typeof(LeagueConstantGridRow)),
        new("constants", "parks", "파크 팩터", typeof(ParkFactorGridRow)),
    };
    public static ViewDefinition Get(string role, string key) => Views.FirstOrDefault(v => v.Role == role && v.Key == key)
        ?? throw new RequestError("지원하지 않는 기록 탭입니다.");

    public static IReadOnlyList<PropertyInfo> Properties(ViewDefinition v) => v.RowType.GetProperties()
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.Name != "Pcode")
        .OrderBy(p => IsWarMetric(p) ? 1 : 0).ThenBy(p => p.MetadataToken).ToArray();

    public static string Label(PropertyInfo p) => p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name;
    // WAR totals belong at the end of every table. WARIP is an innings-based
    // coefficient; rates and player counts keep their existing units and order.
    public static bool IsWarMetric(string label) => label.Contains("WAR", StringComparison.OrdinalIgnoreCase)
        && !label.Contains("WARIP", StringComparison.OrdinalIgnoreCase)
        && !label.Contains('%') && !label.Contains("비중") && !label.Contains("달성률") && !label.Contains("감소율");
    public static bool IsWarMetric(PropertyInfo p) => IsNumber(p)
        && (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) != typeof(int)
        && (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) != typeof(long)
        && IsWarMetric(Label(p));
    public static bool IsNumber(PropertyInfo p)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        return t == typeof(int) || t == typeof(long) || t == typeof(double) || t == typeof(float) || t == typeof(decimal);
    }
    public static string Kind(PropertyInfo p)
    {
        if (!IsNumber(p)) return "text";
        var label = Label(p).Replace("*", "");
        if (p.Name.Contains("WarPerInning", StringComparison.OrdinalIgnoreCase)) return "decimal6";
        if (label.Contains('%') || p.Name.EndsWith("Usage", StringComparison.Ordinal) || label.Contains("사용률") || label.Contains("구사율")) return "percent";
        if (p.Name is "InningsPitched" or "StarterInnings" or "ReliefInnings") return "innings";
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        if (t == typeof(int) || t == typeof(long)) return "integer";
        if (label.Contains("wRC", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Wrc", StringComparison.OrdinalIgnoreCase)) return "decimal1";
        if (IsWarMetric(p)) return "decimal2";
        if (label.EndsWith("/9", StringComparison.Ordinal)) return "decimal2";
        if (label.Contains("AVG") || label.Contains("OBP") || label.Contains("SLG") || label.Contains("OPS") && !label.Contains('+')
            || label.Contains("BABIP") || label.Contains("wOBA") || label.StartsWith("Iso") || label is "ISO" or "R/ePA") return "decimal3";
        if (label.Contains("OPS+") || label.EndsWith("FIP-")) return "integer";
        if (p.Name.Contains("War", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Runs") || p.Name.Contains("Speed") || label.Contains("PF")) return "decimal1";
        return "decimal2";
    }

    public static string Display(PropertyInfo p, object? value)
    {
        if (value is null) return "-";
        if (!IsNumber(p)) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-";
        var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(d)) return "-";
        return Kind(p) switch
        {
            "percent" => (d * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%",
            "innings" => $"{(int)Math.Round(d * 3) / 3}.{(int)Math.Round(d * 3) % 3}",
            "integer" => d.ToString("0", CultureInfo.InvariantCulture),
            "decimal6" => d.ToString("0.000000", CultureInfo.InvariantCulture),
            "decimal1" => d.ToString("0.0", CultureInfo.InvariantCulture),
            "decimal3" => d.ToString("0.000", CultureInfo.InvariantCulture),
            _ => d.ToString("0.00", CultureInfo.InvariantCulture)
        };
    }
    public static double Threshold(PropertyInfo p, double input)
    {
        if (Kind(p) == "percent") return input / 100d;
        if (Kind(p) != "innings") return input;
        var whole = Math.Truncate(input);
        var outs = Math.Round((input - whole) * 10);
        if (input < 0 || outs is < 0 or > 2 || Math.Abs(input - (whole + outs / 10)) > 1e-7)
            throw new RequestError("IP 조건은 6.0, 6.1, 6.2처럼 야구 이닝 표기로 입력하세요.");
        return whole + outs / 3;
    }
}
