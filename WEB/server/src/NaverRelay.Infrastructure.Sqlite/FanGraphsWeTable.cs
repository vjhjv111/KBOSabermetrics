using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace NaverRelay.Infrastructure.Sqlite;

public static class FanGraphsWeTable
{
    public const string ExpectedSha256 = "a32732a0eee49d7ee011c019c5ed4e9b277db4ca1872ad92132d45e28ac1c6c6";
    private static readonly Lazy<IReadOnlyDictionary<string,double>> Table = new(Read);
    public static string Sha256 => ExpectedSha256;
    public static IReadOnlyDictionary<string,double> Cells => Table.Value;
    public static void Validate() => _ = Cells;
    private static IReadOnlyDictionary<string,double> Read()
    {
        using var stream=typeof(FanGraphsWeTable).Assembly.GetManifestResourceStream("FanGraphsWe45.json")
            ?? throw new InvalidDataException("Bundled FanGraphs WE table is missing.");
        using var bytes=new MemoryStream();stream.CopyTo(bytes);var payload=bytes.ToArray();
        if(!Convert.ToHexString(SHA256.HashData(payload)).Equals(ExpectedSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bundled FanGraphs WE table hash mismatch.");
        using var doc=JsonDocument.Parse(payload);var root=doc.RootElement;
        if(root.GetProperty("runEnvironment").GetDouble()!=4.5 || root.GetProperty("source").GetString()!=EstimatedWpaModel.Source)
            throw new InvalidDataException("Unexpected FanGraphs WE source/run environment.");
        var states=root.GetProperty("states");var result=new Dictionary<string,double>();
        foreach(var cell in states.EnumerateObject())
        {
            var value=cell.Value.GetDouble();
            if(!double.IsFinite(value) || value is <0 or >1 || !result.TryAdd(cell.Name,value))
                throw new InvalidDataException("Invalid or duplicate FanGraphs WE cell.");
        }
        if(result.Count!=9072)throw new InvalidDataException("Incomplete FanGraphs WE table.");
        for(var inning=1;inning<=9;inning++)foreach(var side in new[]{"H","A"})
        for(var outs=0;outs<3;outs++)for(var bases=0;bases<8;bases++)for(var lead=-10;lead<=10;lead++)
            if(!result.ContainsKey(FormattableString.Invariant($"{inning}:{side}:{outs}:{bases}:{lead}")))
                throw new InvalidDataException("Missing FanGraphs WE state.");
        return result;
    }
}
