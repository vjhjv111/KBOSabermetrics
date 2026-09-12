using System.Text.Json;

namespace NaverSabermetrics.Web;

public sealed class DiamondBatter
{
    public string Id { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public double Pa { get; set; }
    public double Ab { get; set; }
    public double H { get; set; }
    public double Hr { get; set; }
    public double So { get; set; }
    public double Slg { get; set; }
    public double Avg { get; set; }
    public double Ops { get; set; }
    public DiamondProfile? Profile { get; set; }
    public DiamondDiscipline? Discipline { get; set; }
    public string? SampleNote { get; set; }
}
public sealed class DiamondPitcher
{
    public string Id { get; set; } = "";
    public string PlayerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Team { get; set; } = "";
    public double Tbf { get; set; }
    public double Bb { get; set; }
    public double So { get; set; }
    public double Outs { get; set; }
    public double Er { get; set; }
    public double? Era { get; set; }
    public double? Whip { get; set; }
    public DiamondProfile? Profile { get; set; }
    public DiamondDiscipline? Discipline { get; set; }
    public IReadOnlyList<DiamondArsenal> Arsenal { get; set; } = [];
    public string ArsenalSource { get; set; } = "default";
    public string? SampleNote { get; set; }
}
public sealed class DiamondProfile
{
    public string BatsThrows { get; set; } = "";
    public double? HeightCm { get; set; }
    public string? Throws { get; set; }
    public string? Bats { get; set; }
    public string? Delivery { get; set; }
}
public sealed class DiamondDiscipline
{
    public double? ZonePitchRate { get; set; }
    public double? ZoneSwingRate { get; set; }
    public double? ChaseRate { get; set; }
    public double? ZoneContactRate { get; set; }
    public double? OutZoneContactRate { get; set; }
}
public sealed class DiamondSourcePlayer
{
    public DiamondProfile? Profile { get; set; }
    public DiamondDiscipline? Discipline { get; set; }
}

/// <summary>Match-scoped player data. Bundled data supports existing matches and regression fixtures.</summary>
public sealed class DiamondData
{
    private readonly Dictionary<string, DiamondBatter> _batters;
    private readonly Dictionary<string, DiamondPitcher> _pitchers;
    private readonly Dictionary<string, DiamondProfile> _profiles;
    private readonly Dictionary<string, DiamondSourcePlayer> _batterSource;
    private readonly Dictionary<string, DiamondSourcePlayer> _pitcherSource;
    private readonly Dictionary<string, List<DiamondArsenal>> _arsenals;
    private readonly Dictionary<string, List<DiamondCapsule>> _colliders;
    private static readonly DiamondArsenal[] DefaultArsenal =
        [new("fastball", 145, 55), new("slider", 133, 30), new("curve", 122, 15)];

    public DiamondData(string directory)
    {
        using var players = Read(directory, "players.json");
        _batters = players.RootElement.GetProperty("batters").Deserialize<List<DiamondBatter>>(DiamondJson.Options)!.ToDictionary(x => x.Id);
        _pitchers = players.RootElement.GetProperty("pitchers").Deserialize<List<DiamondPitcher>>(DiamondJson.Options)!.ToDictionary(x => x.Id);
        using var profiles = Read(directory, "player-profiles.json");
        _profiles = profiles.RootElement.GetProperty("players").Deserialize<Dictionary<string, DiamondProfile>>(DiamondJson.Options)!;
        using var source = Read(directory, "action-data.json");
        _batterSource = source.RootElement.GetProperty("batters").Deserialize<Dictionary<string, DiamondSourcePlayer>>(DiamondJson.Options)!;
        _pitcherSource = source.RootElement.GetProperty("pitchers").Deserialize<Dictionary<string, DiamondSourcePlayer>>(DiamondJson.Options)!;
        using var arsenal = Read(directory, "arsenals.json");
        _arsenals = arsenal.RootElement.Deserialize<Dictionary<string, List<DiamondArsenal>>>(DiamondJson.Options)!;
        using var colliders = Read(directory, "batter-colliders.json");
        _colliders = colliders.RootElement.Deserialize<Dictionary<string, List<DiamondCapsule>>>(DiamondJson.Options)!;
        if (_batters.Values.Any(x => x.Pa <= 0) || _pitchers.Values.Any(x => x.Tbf <= 0)
            || !_colliders.ContainsKey("left") || !_colliders.ContainsKey("right"))
            throw new InvalidDataException("게임 선수 자료를 확인하세요.");
    }
    private DiamondData(DiamondData legacy, DiamondRosterSelection roster)
    {
        _batters = new() { [roster.Batter.Id] = roster.Batter };
        _pitchers = new() { [roster.Pitcher.Id] = roster.Pitcher };
        _profiles = [];
        _batterSource = new() { [roster.Batter.Id] = new() { Profile = roster.Batter.Profile, Discipline = roster.Batter.Discipline } };
        _pitcherSource = new() { [roster.Pitcher.Id] = new() { Profile = roster.Pitcher.Profile, Discipline = roster.Pitcher.Discipline } };
        _arsenals = new() { [roster.Pitcher.Id] = roster.Pitcher.Arsenal.ToList() };
        _colliders = legacy._colliders;
    }
    public DiamondData ForRoster(DiamondRosterSelection? roster) => roster is null ? this : new(this, roster);
    private static JsonDocument Read(string path, string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(path, file)));
    public DiamondBatter Batter(string id) => _batters.GetValueOrDefault(id) ?? throw new DiamondInputError("타자를 선택해 주세요.");
    public DiamondPitcher Pitcher(string id) => _pitchers.GetValueOrDefault(id) ?? throw new DiamondInputError("투수를 선택해 주세요.");
    public DiamondProfile? Profile(string id, string side) => _profiles.GetValueOrDefault(id)
        ?? (side == "batter" ? _batterSource : _pitcherSource).GetValueOrDefault(id)?.Profile;
    public DiamondDiscipline? Discipline(string id, string side) =>
        (side == "batter" ? _batterSource : _pitcherSource).GetValueOrDefault(id)?.Discipline;
    public IReadOnlyList<DiamondArsenal> Arsenal(string id) => _arsenals.TryGetValue(id, out var list) && list.Count > 0 ? list : DefaultArsenal;
    public IReadOnlyList<DiamondCapsule> Colliders(bool left) => _colliders[left ? "left" : "right"];
    public bool ThrowsLeft(string id)
    {
        var p = Profile(id, "pitcher");
        return p?.Throws is { Length: > 0 } hand ? hand == "L" : p?.BatsThrows.StartsWith("좌", StringComparison.Ordinal) == true;
    }
    public bool Underhand(string id)
    {
        var p = Profile(id, "pitcher");
        return p?.Delivery == "underhand" || p?.BatsThrows.Contains('언') == true;
    }
    public bool BatsLeft(string batter, string pitcher)
    {
        var p = Profile(batter, "batter");
        var switchHitter = p?.Bats == "S" || p?.BatsThrows.EndsWith("양타", StringComparison.Ordinal) == true;
        return switchHitter ? !ThrowsLeft(pitcher) : p?.Bats is { Length: > 0 } hand ? hand == "L"
            : p?.BatsThrows.EndsWith("좌타", StringComparison.Ordinal) == true;
    }
}
