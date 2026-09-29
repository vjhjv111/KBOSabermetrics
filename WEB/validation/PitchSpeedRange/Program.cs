using Microsoft.Data.Sqlite;
using NaverRelay.Application.Queries;
using NaverRelay.Infrastructure.Sqlite;

if (args.Length != 1) throw new ArgumentException("Pass the read-only DB path.");
var db = new DatabaseCacheService(args[0], webReadOnly: true);
var service = new DatabasePitcherRecordRoomService(db);
await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=args[0], Mode=SqliteOpenMode.ReadOnly }.ToString());
await connection.OpenAsync();
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
foreach (var start in new DateTime?[] { null, new(2026, 9, 1) })
{
    var query = new GameQuery { SeasonYear=2026, StartDate=start };
    var rows = await service.GetPitchTypesAsync(query);
    var row = rows.Single(r=>r.Pcode=="68220" && r.TeamCode=="OB");
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT p.SpeedKmh FROM Pitches p JOIN Games g ON g.GameId=p.GameId
        WHERE g.SeasonYear=2026 AND lower(trim(g.RoundCode))='kbo_r'
          AND p.PitcherPcode='68220' AND p.SpeedKmh>0
          AND ($start='' OR g.GameDate >= $start)
          AND p.PitchType NOT LIKE '%투심%'
          AND (p.PitchType LIKE '%직구%' OR p.PitchType LIKE '%포심%'
               OR lower(p.PitchType) LIKE '%four%' OR lower(p.PitchType) LIKE '%4-seam%')
        """;
    command.Parameters.AddWithValue("$start", start?.ToString("yyyy-MM-dd") ?? "");
    var speeds = new List<double>();
    await using (var reader=await command.ExecuteReaderAsync())
        while(await reader.ReadAsync()) speeds.Add(reader.GetDouble(0));
    Check(speeds.Count>0, "raw sample available");
    Check(row.FourSeamMinSpeed==speeds.Min(), "fastball minimum agrees with raw pitches " + start);
    Check(row.FourSeamMaxSpeed==speeds.Max(), "fastball maximum agrees with raw pitches " + start);
    Check(Math.Abs(row.FourSeamSpeed!.Value-speeds.Average())<1e-8, "mean uses the same sample");
    foreach(var r in rows)
        foreach(var prefix in new[]{"TwoSeam","FourSeam","Cutter","Curve","Slider","Changeup","Sinker","Forkball","Knuckleball","Other"})
        {
            double? Value(string suffix)=>(double?)r.GetType().GetProperty(prefix+suffix)!.GetValue(r);
            var average=Value("Speed"); var min=Value("MinSpeed"); var max=Value("MaxSpeed");
            CheckSilent(average is null ? min is null && max is null : min>0 && min<=average && average<=max);
        }
    Console.WriteLine($"PASS all {rows.Count} pitchers × 10 pitch buckets: range and null invariants");
    Console.WriteLine($"Kwak fastball: {row.FourSeamMinSpeed} / {row.FourSeamSpeed:F2} / {row.FourSeamMaxSpeed} km/h");
}
static void CheckSilent(bool ok) { if(!ok) throw new Exception("Invalid min/mean/max or missing-value handling"); }
