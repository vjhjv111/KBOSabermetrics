using System.Globalization;
using NaverRelay.Application.Statistics;

namespace NaverRelay.Gui.Services;

/// <summary>
/// DataGridView 숫자 표시 형식을 야구 통계 관례에 맞춰 통일합니다.
/// 실제 바인딩 값은 변경하지 않고 화면 표시만 변경합니다.
/// </summary>
internal static class GridNumberFormatter
{
    private static readonly HashSet<string> ThreeDecimalStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "AVG", "OBP", "SLG", "OPS", "BABIP", "Woba", "Iso", "IsoP", "IsoD", "Wpa",
        "PositiveWpa", "NegativeWpa", "BuntAverage", "RunsPerEffectivePa",
        "RbiPerPa", "RunsPerPa", "OpponentAVG", "OpponentOPS", "WinningPercentage",
        "PlateX", "PlateZ", "OpponentOBP",
        "LeftAverage", "LeftCenterAverage", "CenterAverage", "RightCenterAverage", "RightAverage",
        "PullAverage", "OppositeAverage",
        "TwoSeamOpponentAverage", "FourSeamOpponentAverage", "CutterOpponentAverage",
        "CurveOpponentAverage", "SliderOpponentAverage", "ChangeupOpponentAverage",
        "SinkerOpponentAverage", "ForkballOpponentAverage", "KnuckleballOpponentAverage", "OtherOpponentAverage",
        "TwoSeamOpponentSlugging", "FourSeamOpponentSlugging", "CutterOpponentSlugging",
        "CurveOpponentSlugging", "SliderOpponentSlugging", "ChangeupOpponentSlugging",
        "SinkerOpponentSlugging", "ForkballOpponentSlugging", "KnuckleballOpponentSlugging", "OtherOpponentSlugging",
    };

    private static readonly HashSet<string> PercentageStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "WalkRate", "StrikeoutRate", "WhiffRate", "CswRate",
        "StrikeoutMinusWalkRate", "LobRate",
        "SwingRate", "ContactRate", "ZoneSwingRate", "ChaseRate",
        "ZoneContactRate", "OutZoneContactRate", "SwingingStrikeRate",
        "FirstPitchSwingRate", "UsageRate", "StrikeRate", "SuccessRate",
        "ExtraBaseHitRate", "HomeRunPerExtraBaseHit",
        "GroundBallRate", "FlyBallRate", "LineDriveRate", "InfieldFlyRate", "OutfieldFlyRate",
        "HomeRunPerFlyBall", "InfieldHitRate", "LeftRate", "LeftCenterRate", "CenterRate",
        "RightCenterRate", "RightRate", "InfieldRate", "PullRate", "OppositeRate",
        "ThreeTrueOutcomeRate", "TwoSeamUsage", "FourSeamUsage", "CutterUsage", "CurveUsage",
        "SliderUsage", "ChangeupUsage", "SinkerUsage", "ForkballUsage", "KnuckleballUsage", "OtherUsage",
        "StolenBaseSuccessRate", "QualityStartRate", "QualityStartPlusRate", "TeamWinRate",
        "CalledStrikeRate", "WhiffPerPitch", "FirstPitchStrikeRate", "FirstPitchWhiffRate",
        "PutAwayRate", "ZonePitchRate", "OutZonePitchRate", "HeartPitchRate", "HeartSwingRate",
        "CalledStrikeoutRate",
    };

    private static readonly HashSet<string> TwoDecimalStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "WalkToStrikeout", "StrikeoutsPerNine", "WalksPerNine", "HomeRunsPerNine",
        "Fip", "Xfip", "PitchesPerPa", "ReplacementRa9", "ERA", "WHIP",
        // KBO 투수 WAR v3
        "IfFip", "FipR9", "ParkAdjustedFipR9", "DynamicRunsPerWin",
        "GmLi", "LeverageMultiplier", "StarterReplacementWins",
        "RelieverReplacementWins", "WarBeforeCorrection", "LeagueCorrection",
        "StarterReplacementFipMinus", "RelieverReplacementFipMinus",
        "ParkAdjustedRa9", "Ra9RunsPerWin", "Ra9WarBeforeCorrection", "Ra9LeagueCorrection",
        "AverageLeverageIndex", "WpaPerLi", "GroundBallToFlyBall",
        "PlateAppearancesPerHomeRun", "AtBatsPerHomeRun", "SluggingToAverage",
        "PowerSpeedNumber", "TotalAverage", "SecondaryAverage", "RunsCreated27",
        "StrikeoutToWalk", "PitchesPerGame", "PitchesPerInning", "InningsPerStart",
        "PitchesPerStart", "InningsPerReliefGame", "PitchesPerReliefGame", "RunSupportPerNine",
        "PitchEntropy", "NormalizedPitchEntropy",
        "TwoSeamValue", "FourSeamValue", "CutterValue", "CurveValue", "SliderValue",
        "ChangeupValue", "SinkerValue", "ForkballValue", "KnuckleballValue", "OtherValue",
        "TwoSeamValuePer100", "FourSeamValuePer100", "CutterValuePer100", "CurveValuePer100",
        "SliderValuePer100", "ChangeupValuePer100", "SinkerValuePer100", "ForkballValuePer100",
        "KnuckleballValuePer100", "OtherValuePer100",
    };

    private static readonly HashSet<string> OneDecimalStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpeedKmh", "AverageSpeed", "InningsPitched", "Wraa", "Wrc",
        "BattingRuns", "RunningRuns", "FieldingRuns", "PositionRuns", "OffensiveRuns", "ReplacementRuns",
        "RunsCreated",
        "RunsAboveReplacement", "RunsPerWin", "War", "Ra9War", "BlendWar",
        "BatterWar", "PitcherWar", "PitcherRa9War", "PitcherBlendWar", "TotalWar", "BatterRAR", "PitcherRAR",
        // KBO 투수 WAR v3 / 파크 팩터
        "ParkFactor", "RawFipFactor", "UsedFipFactor", "Innings",
        "StarterInnings", "ReliefInnings", "StarterRunsAboveAverage", "ReliefRunsAboveAverage",
        "RunsAboveAverage", "StarterReplacementRuns", "ReliefReplacementRuns", "StarterRAR", "ReliefRAR",
        "StarterWAA", "ReliefWAA", "WAA",
        "RAR", "StarterWar", "ReliefWar",
        "TwoSeamSpeed", "FourSeamSpeed", "CutterSpeed", "CurveSpeed", "SliderSpeed",
        "ChangeupSpeed", "SinkerSpeed", "ForkballSpeed", "KnuckleballSpeed", "OtherSpeed",
    };


    private static readonly HashSet<string> SixDecimalStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "WarPerInningCorrection", "Ra9WarPerInningCorrection",
    };

    private static readonly HashSet<string> IntegerIndexStats = new(StringComparer.OrdinalIgnoreCase)
    {
        "WrcPlus", "OpsPlus", "FipMinus", "XfipMinus",
    };

    public static void Apply(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            var property = string.IsNullOrWhiteSpace(column.DataPropertyName)
                ? column.Name
                : column.DataPropertyName;

            column.DefaultCellStyle.NullValue = "-";
            column.DefaultCellStyle.Alignment = IsNumericColumn(column)
                ? DataGridViewContentAlignment.MiddleRight
                : DataGridViewContentAlignment.MiddleLeft;

            if (SixDecimalStats.Contains(property))
            {
                column.DefaultCellStyle.Format = "0.000000";
            }
            else if (PercentageStats.Contains(property))
            {
                // 내부 값 0.105 -> 화면 10.5%
                column.DefaultCellStyle.Format = "0.0%";
            }
            else if (ThreeDecimalStats.Contains(property))
            {
                column.DefaultCellStyle.Format = "0.000";
            }
            else if (TwoDecimalStats.Contains(property))
            {
                column.DefaultCellStyle.Format = "0.00";
            }
            else if (OneDecimalStats.Contains(property))
            {
                column.DefaultCellStyle.Format = "0.0";
            }
            else if (IntegerIndexStats.Contains(property))
            {
                column.DefaultCellStyle.Format = "0";
            }
            else if (IsFloatingPointColumn(column))
            {
                // 새 실수형 통계가 추가되더라도 긴 소수점이 그대로 노출되지 않도록
                // 기본적으로 소수 둘째 자리까지만 표시합니다.
                column.DefaultCellStyle.Format = "0.00";
            }
        }
    }

    /// <summary>
    /// 리그 상수 탭의 값은 항목마다 단위가 달라 행 단위로 표시 형식을 결정합니다.
    /// </summary>
    public static void FormatCell(DataGridView grid, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Value is null)
        {
            return;
        }

        var column = grid.Columns[e.ColumnIndex];
        if (!string.Equals(column.DataPropertyName, nameof(LeagueConstantGridRow.Value), StringComparison.Ordinal))
        {
            return;
        }

        if (grid.Rows[e.RowIndex].DataBoundItem is not LeagueConstantGridRow row || !row.Value.HasValue)
        {
            return;
        }

        var metric = row.Metric ?? string.Empty;
        var value = row.Value.Value;

        if (metric.Contains("WARIP", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = value.ToString("0.000000", CultureInfo.CurrentCulture);
        }
        else if (metric.Contains('%') ||
            metric.Contains("Rate", StringComparison.OrdinalIgnoreCase) ||
            metric.Contains("달성률", StringComparison.OrdinalIgnoreCase) ||
            metric.Contains("R/PA", StringComparison.OrdinalIgnoreCase) ||
            metric.Contains("HR/FB", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = value.ToString("0.0%", CultureInfo.CurrentCulture);
        }
        else if (metric.Contains("wOBA", StringComparison.OrdinalIgnoreCase) ||
                 metric.Contains("가중치", StringComparison.OrdinalIgnoreCase) ||
                 metric.Contains("Weight", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = value.ToString("0.000", CultureInfo.CurrentCulture);
        }
        else if (metric.Contains("상수", StringComparison.OrdinalIgnoreCase) ||
                 metric.Contains("Scale", StringComparison.OrdinalIgnoreCase) ||
                 metric.Contains("FIP", StringComparison.OrdinalIgnoreCase))
        {
            e.Value = value.ToString("0.00", CultureInfo.CurrentCulture);
        }
        else
        {
            e.Value = value.ToString("0.000", CultureInfo.CurrentCulture);
        }

        e.FormattingApplied = true;
    }

    private static bool IsFloatingPointColumn(DataGridViewColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.ValueType ?? typeof(object)) ?? column.ValueType;
        return type == typeof(float) || type == typeof(double) || type == typeof(decimal);
    }

    private static bool IsNumericColumn(DataGridViewColumn column)
    {
        var type = Nullable.GetUnderlyingType(column.ValueType ?? typeof(object)) ?? column.ValueType;
        return type == typeof(byte) || type == typeof(short) || type == typeof(int) ||
               type == typeof(long) || type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }
}
