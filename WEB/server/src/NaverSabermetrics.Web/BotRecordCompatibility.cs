namespace NaverSabermetrics.Web;

public static class BotRecordCompatibility
{
    public static string? NormalizeStat(string role, string? stat)
    {
        var key = stat?.Trim();
        if (role == "batter" && IsWrcPlus(key)) return "WrcPlusParkAdjusted";
        return ViewRegistry.Properties(ViewRegistry.Get(role, "basic"))
            .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;
    }

    private static bool IsWrcPlus(string? stat) =>
        string.Equals(stat, "WrcPlus", StringComparison.OrdinalIgnoreCase)
        || string.Equals(stat, "WrcPlusParkAdjusted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(stat, "wRC+", StringComparison.OrdinalIgnoreCase);

    public static TablePage AddCompatibilityCells(TablePage page, string? requestedStat)
    {
        // Older bot clients read cells.WrcPlus even though the website now only
        // exposes WrcPlusParkAdjusted. Both keys must carry the same public value.
        return page with { Rows = page.Rows.Select(row =>
        {
            var cells = new Dictionary<string, string>(row.Cells);
            if (cells.TryGetValue("WrcPlusParkAdjusted", out var value))
            {
                cells["WrcPlus"] = value;
                if (IsWrcPlus(requestedStat?.Trim())) cells[requestedStat!.Trim()] = value;
            }
            return row with { Cells = cells };
        }).ToArray() };
    }
}
