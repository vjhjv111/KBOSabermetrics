using System.Globalization;
using Microsoft.Data.Sqlite;

namespace NaverSabermetrics.Web;

public sealed partial class DiamondRosterService
{
    private const int MaximumPitchBins = 2048;
    private const int MinimumObservedPitchSamples = 20;
    private readonly Dictionary<string, PitchingCacheEntry> _pitchingCache = new(StringComparer.Ordinal);
    private sealed record PitchingCacheEntry(DateTime At, DiamondPitchingProfile Profile);
    private sealed record PitchSample(string Type, double X, double Y, double Velocity,
        string? Hand, int Balls, int Strikes, bool Hbp);
    private readonly record struct PitchBinKey(string Type, string? Hand, int Balls, int Strikes,
        bool Hbp, int XBand, int YBand, double X, double Y);
    private sealed class PitchBinAccumulator
    {
        public int Count;
        public double X, Y, Velocity;
        public void Add(PitchSample p)
        {
            Count++;
            X += (p.X - X) / Count;
            Y += (p.Y - Y) / Count;
            Velocity += (p.Velocity - Velocity) / Count;
        }
    }

    /// <summary>Reads only the selected pitcher; match creation pins this profile's revision and data.</summary>
    public DiamondPitchingProfile SelectPitching(int season, string pitcherId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(pitcherId) || pitcherId.Length > 80)
            throw new DiamondInputError("기록실 선수 목록에서 투수를 선택해 주세요.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var roster = Get(season, token);
            var pitcher = roster.Pitchers.FirstOrDefault(x => x.Id == pitcherId)
                ?? throw new DiamondInputError("선택한 시즌의 투수를 다시 선택해 주세요.");
            _gate.Wait(token);
            try
            {
                var stamp = Stamp();
                if (!_cache.TryGetValue(season, out var rosterCache) || !ReferenceEquals(rosterCache.Roster, roster)
                    || rosterCache.Stamp != stamp) continue;
                var key = roster.Revision + ":" + pitcherId;
                if (_pitchingCache.TryGetValue(key, out var cached)) return CopyPitching(cached.Profile);
                var profile = LoadPitching(roster, pitcher, token);
                if (Stamp() != stamp) continue;
                while (_pitchingCache.Count >= 24) _pitchingCache.Remove(_pitchingCache.MinBy(x => x.Value.At).Key);
                _pitchingCache[key] = new(DateTime.UtcNow, profile);
                return CopyPitching(profile);
            }
            catch (OperationCanceledException) { throw; }
            catch (DiamondInputError) { throw; }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
            { token.ThrowIfCancellationRequested(); _pitchingCache.Clear(); throw new DiamondInputError("기록실 DB의 투구 분포를 불러오지 못했습니다. DB 준비 상태를 확인해 주세요.", 503); }
            finally { _gate.Release(); }
        }
        throw new DiamondInputError("기록 DB를 갱신 중입니다. 잠시 후 선수를 다시 선택해 주세요.", 503);
    }

    private static DiamondPitchingProfile CopyPitching(DiamondPitchingProfile value) => new()
    {
        Season = value.Season, PitcherId = value.PitcherId, Revision = value.Revision, Source = value.Source,
        TotalPitchCount = value.TotalPitchCount, MeasuredPitchCount = value.MeasuredPitchCount,
        UsablePitchCount = value.UsablePitchCount, HitByPitchCount = value.HitByPitchCount,
        HitByPitchRate = value.HitByPitchRate, GridSize = value.GridSize, SampleNote = value.SampleNote,
        Bins = value.Bins.Select(b => b with { }).ToArray()
    };

    private DiamondPitchingProfile LoadPitching(DiamondRoster roster, DiamondPitcher pitcher, CancellationToken token)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3 }.ToString());
        c.Open(); using var tx = c.BeginTransaction(deferred: true);
        var notes = new List<string>(); var samples = new List<PitchSample>();
        var total = 0; var measured = 0; var terminalHbp = 0; int? officialHbp = null;
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Read(c, tx, "PRAGMA table_info(Pitches)", 0, token, r => columns.Add(r.GetString(1)));
        var baseColumns = new[] { "GameId", "PitcherPcode", "PitchType", "SpeedKmh" };
        var coordinates = new[] { "CrossPlateX", "CalculatedCrossPlateZ", "TopStrikeZone", "BottomStrikeZone" };
        if (Has(c, tx, "PitcherGameStats", ["GameId", "Pcode", "HasFinalLine", "FinalHBP", "PaHBP"], token))
            ReadPitching(c, tx, $"SELECT SUM(CASE WHEN p.HasFinalLine=1 THEN p.FinalHBP ELSE p.PaHBP END) HBP FROM PitcherGameStats p JOIN Games g ON g.GameId=p.GameId WHERE {RegularGames} AND p.Pcode=$pitcher", roster.Season, pitcher.PlayerId, token,
                r => { if (r["HBP"] is not DBNull) officialHbp = Convert.ToInt32(r["HBP"], CultureInfo.InvariantCulture); });
        if (baseColumns.All(columns.Contains))
        {
            var hasCoordinates = coordinates.All(columns.Contains);
            var hasHbp = columns.Contains("PlateAppearanceId") && columns.Contains("ActualPitchIndex") && columns.Contains("PitchEventId")
                && Has(c, tx, "PlateAppearances", ["PlateAppearanceId", "ResultType", "Status", "IsOfficial"], token);
            var coordinateSql = hasCoordinates ? "p.CrossPlateX X,p.CalculatedCrossPlateZ Z,p.TopStrikeZone Top,p.BottomStrikeZone Bottom" : "NULL X,NULL Z,NULL Top,NULL Bottom";
            var handSql = columns.Contains("BatterStance") ? "p.BatterStance" : "NULL";
            var ballsSql = columns.Contains("BallsBefore") ? "p.BallsBefore" : "NULL";
            var strikesSql = columns.Contains("StrikesBefore") ? "p.StrikesBefore" : "NULL";
            // A plate appearance can span two pitchers. Check its final pitch across all pitchers, not just this selection.
            var hbpSql = hasHbp ? """
                CASE WHEN pa.ResultType=9 AND pa.Status=0 AND pa.IsOfficial=1 AND NOT EXISTS
                  (SELECT 1 FROM Pitches later WHERE later.PlateAppearanceId=p.PlateAppearanceId AND
                    (later.ActualPitchIndex>p.ActualPitchIndex OR
                     (later.ActualPitchIndex=p.ActualPitchIndex AND later.PitchEventId>p.PitchEventId))) THEN 1 ELSE 0 END
                """ : "0";
            var join = hasHbp ? "LEFT JOIN PlateAppearances pa ON pa.PlateAppearanceId=p.PlateAppearanceId" : "";
            ReadPitching(c, tx, $"SELECT p.PitchType Type,p.SpeedKmh Velocity,{coordinateSql},{handSql} Hand,{ballsSql} Balls,{strikesSql} Strikes,{hbpSql} HBP FROM Pitches p JOIN Games g ON g.GameId=p.GameId {join} WHERE {RegularGames} AND p.PitcherPcode=$pitcher ORDER BY g.GameDate,g.GameId", roster.Season, pitcher.PlayerId, token, r =>
            {
                total++; var hbp = N(r, "HBP") > 0; if (hbp) terminalHbp++;
                var x = Finite(r, "X"); var z = Finite(r, "Z"); var top = Finite(r, "Top"); var bottom = Finite(r, "Bottom");
                if (x == null || z == null || top == null || bottom == null || top <= bottom) return;
                var nx = x.Value / (8.5 / 12.0); var ny = 2 * (z.Value - bottom.Value) / (top.Value - bottom.Value) - 1;
                if (!double.IsFinite(nx) || !double.IsFinite(ny)) return;
                measured++;
                var velocity = Finite(r, "Velocity");
                if (velocity is not > 0 || !PitchNames.TryGetValue(S(r, "Type").Trim(), out var type)) return;
                var hand = S(r, "Hand").Trim().ToUpperInvariant();
                samples.Add(new(type, nx, ny, velocity.Value, hand is "L" or "R" ? hand : null,
                    CountValue(r, "Balls", 3), CountValue(r, "Strikes", 2), hbp));
            });
            if (!hasCoordinates) notes.Add("투구 좌표 열 미수집: 게임 기본 코스 적용");
            if (!hasHbp) notes.Add("타석별 마지막 투구 미수집: 실측 사구 위치 분류 불가");
        }
        else notes.Add("투구 자료 미수집: 게임 기본 코스 적용");
        if (total > measured) notes.Add($"좌표 미측정 {total - measured}구 제외");
        if (measured > samples.Count) notes.Add($"미지원 구종·구속 미측정 {measured - samples.Count}구 제외");
        if (samples.Any(x => x.Hand == null || x.Balls < 0 || x.Strikes < 0)) notes.Add("일부 투구의 타자 방향·볼카운트 미수집");
        var hbpCount = Math.Max(0, officialHbp ?? terminalHbp);
        if (officialHbp != null && officialHbp != terminalHbp) notes.Add("최종 사구 기록과 위치가 연결된 사구 표본 수가 다름");
        var source = samples.Count >= MinimumObservedPitchSamples ? "observed" : "default";
        if (source == "default") notes.Add("사용 가능한 실측 투구 20구 미만: 게임 기본 코스 적용");
        else if (samples.Count < 100) notes.Add("실측 투구 100구 미만의 적은 표본");
        if (hbpCount > total) notes.Add("사구 기록이 수집 투구 수보다 많음: 사구율 미적용");
        var (grid, bins, reducedContext) = source == "observed" ? PitchBins(samples, token) : (0.0, Array.Empty<DiamondPitchLocationBin>(), false);
        if (reducedContext) notes.Add("분포 용량 제한에 따라 볼카운트·타자 방향의 세부 조건을 합산");
        return new() { Season = roster.Season, PitcherId = pitcher.Id, Revision = roster.Revision,
            Source = source, TotalPitchCount = total, MeasuredPitchCount = measured, UsablePitchCount = samples.Count,
            HitByPitchCount = hbpCount, HitByPitchRate = total > 0 && hbpCount <= total && officialHbp != null ? (double)hbpCount / total : null,
            GridSize = grid, Bins = bins, SampleNote = Note(notes) };
    }

    private static (double Grid, DiamondPitchLocationBin[] Bins, bool ReducedContext) PitchBins(List<PitchSample> samples, CancellationToken token)
    {
        // Bound both work and serialized output, even for malformed or extremely large coordinate ranges.
        for (var step = 0; step < 11; step++)
        {
            var grid = step < 8 ? 0.25 * Math.Pow(2, step) : 32;
            var keepCounts = step < 8; var keepHand = step < 9; var keepCells = step < 10;
            token.ThrowIfCancellationRequested(); var groups = new Dictionary<PitchBinKey, PitchBinAccumulator>();
            foreach (var p in samples)
            {
                // HBP and ordinary pitches stay in separate bins so the engine can calibrate body collisions independently.
                // Preserve all nine zone regions: averaging pitches from two different ball regions can otherwise create a strike.
                static int Band(double v) => v < -1 ? -1 : v > 1 ? 1 : 0;
                var key = new PitchBinKey(p.Type, keepHand ? p.Hand : null, keepCounts ? p.Balls : -1,
                    keepCounts ? p.Strikes : -1, p.Hbp, Band(p.X), Band(p.Y),
                    keepCells ? Math.Floor(p.X / grid) : 0, keepCells ? Math.Floor(p.Y / grid) : 0);
                if (!groups.TryGetValue(key, out var bin)) groups[key] = bin = new();
                bin.Add(p);
            }
            if (groups.Count <= MaximumPitchBins)
            {
                var bins = groups.OrderBy(x => x.Key.Type, StringComparer.Ordinal).ThenBy(x => x.Key.Hand, StringComparer.Ordinal)
                    .ThenBy(x => x.Key.Balls).ThenBy(x => x.Key.Strikes).ThenBy(x => x.Key.Hbp)
                    .ThenBy(x => x.Key.XBand).ThenBy(x => x.Key.YBand).ThenBy(x => x.Key.X).ThenBy(x => x.Key.Y)
                    .Select(x => new DiamondPitchLocationBin(x.Key.Type, x.Value.X, x.Value.Y, x.Value.Velocity,
                        x.Key.Hand, x.Key.Balls, x.Key.Strikes, x.Value.Count, x.Key.Hbp ? x.Value.Count : 0)).ToArray();
                return (keepCells ? grid : 0, bins, !keepCounts);
            }
        }
        // Last step has at most seven types * two HBP states * nine zone regions = 126 groups.
        throw new InvalidOperationException("투구 분포의 구종 범위를 확인해 주세요.");
    }

    private static double? Finite(SqliteDataReader r, string name)
    { if (r[name] is DBNull) return null; var n = Convert.ToDouble(r[name], CultureInfo.InvariantCulture); return double.IsFinite(n) ? n : null; }
    private static int CountValue(SqliteDataReader r, string name, int max)
    { var n = Finite(r, name); return n is >= 0 && n <= max && n == Math.Truncate(n.Value) ? (int)n.Value : -1; }
    private static void ReadPitching(SqliteConnection c, SqliteTransaction tx, string sql, int year, string pitcher,
        CancellationToken token, Action<SqliteDataReader> row)
    {
        token.ThrowIfCancellationRequested(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = sql; cmd.CommandTimeout = 8;
        cmd.Parameters.AddWithValue("$year", year); cmd.Parameters.AddWithValue("$pitcher", pitcher);
        using var registration = token.Register(cmd.Cancel); using var reader = cmd.ExecuteReader();
        while (reader.Read()) { token.ThrowIfCancellationRequested(); row(reader); }
        token.ThrowIfCancellationRequested();
    }
}
