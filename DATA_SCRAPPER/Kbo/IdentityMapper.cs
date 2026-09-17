using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KboRelayDownloader;

public sealed record RowIdentity(int RowNumber, string Name, string? NaverPcode, string? BirthDate,
    string? BackNumber, string MatchStatus, IReadOnlyList<string> CandidatePcodes);
public sealed record IdentityMappingSummary(string Source, string SourceFileName, string SourceSha256,
    string NaverGameId, DateTimeOffset MappedAt, string Method, int Matched, int Unresolved);

/// <summary>Only reads identity metadata from Naver. Never reads its statistics or play results.</summary>
public static class IdentityMapper
{
    sealed record Candidate(string Side, string Role, string Name, string? Pcode,
        string? Birth, string? Number, bool InvalidMetadata);

    static string? Str(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch { JsonValueKind.String => p.GetString()?.Trim(), JsonValueKind.Number => p.GetRawText(), _ => null };
    }

    static string NormalizeName(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ");

    static JsonElement Object(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"네이버 선수정보에 {name} 객체가 없습니다.");
        return value;
    }

    static string NormalizeGameId(string? value)
    {
        try { return GameRequest.Parse(value ?? "").GameId; }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new InvalidDataException("네이버 JSON의 경기 ID를 확인할 수 없습니다.", ex); }
    }

    public static RelayDocument Enrich(RelayDocument kbo, string naverJson, string sourceFileName)
    {
        var box = kbo.BoxScore ?? throw new InvalidDataException("KBO 박스스코어가 없습니다.");
        using var source = JsonDocument.Parse(naverJson);
        var result = Object(source.RootElement, "result");
        var game = Object(result, "game");
        var relay = Object(result, "textRelayData");
        if (NormalizeGameId(Str(game, "gameId")) != kbo.GameId || NormalizeGameId(Str(relay, "gameId")) != kbo.GameId)
            throw new InvalidDataException("KBO와 네이버 JSON의 경기 ID가 다릅니다. ID를 연결하지 않았습니다.");
        if (kbo.LeagueId != "1" || Str(game, "awayTeamCode") != box.Away.TeamCode || Str(game, "homeTeamCode") != box.Home.TeamCode)
            throw new InvalidDataException("KBO와 네이버의 리그 또는 홈/원정 팀이 다릅니다.");
        var round = Str(game, "roundCode");
        if (round is not null && ((kbo.SeriesId == "0" && round != "kbo_r") || (kbo.SeriesId != "0" && round == "kbo_r")))
            throw new InvalidDataException("KBO와 네이버의 정규시즌 구분이 다릅니다.");

        var candidates = new List<Candidate>();
        foreach (var side in new[] { "away", "home" })
        {
            var lineup = Object(relay, side + "Lineup");
            foreach (var role in new[] { "batter", "pitcher" })
            {
                if (!lineup.TryGetProperty(role, out var array) || array.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"네이버 {side} {role} 라인업이 없습니다.");
                foreach (var person in array.EnumerateArray())
                {
                    var name = Str(person, "name");
                    if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("네이버 라인업에 이름 없는 선수가 있습니다.");
                    var pcode = Str(person, "pcode");
                    bool invalid = string.IsNullOrEmpty(pcode) || !Regex.IsMatch(pcode, @"^\d+$");
                    var birthRaw = Str(person, "birth");
                    string? birth = null;
                    if (!string.IsNullOrWhiteSpace(birthRaw))
                    {
                        if (DateTime.TryParseExact(birthRaw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            birth = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        else invalid = true;
                    }
                    candidates.Add(new(side, role, NormalizeName(name), pcode, birth, Str(person, "backnum"), invalid));
                }
            }
        }

        // Same ID appearing with contradictory identity data is not used for assignment.
        var conflictingIds = candidates.Where(c => !string.IsNullOrEmpty(c.Pcode)).GroupBy(c => c.Pcode!)
            .Where(g => g.Any(c => c.InvalidMetadata) || g.Select(c => c.Name).Distinct().Count() > 1 ||
                g.Select(c => c.Side).Distinct().Count() > 1 ||
                g.Select(c => c.Birth).Where(v => !string.IsNullOrEmpty(v)).Distinct().Count() > 1 ||
                g.Select(c => c.Number).Where(v => !string.IsNullOrEmpty(v)).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();
        int matched = 0, unresolved = 0;

        StatTable Map(StatTable table, string side, string role, string nameColumn)
        {
            var names = table.Rows.Select(r => r.TryGetValue(nameColumn, out var n) ? NormalizeName(n) : "").ToArray();
            var repeatedNames = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            var identities = new List<RowIdentity>();
            for (int i = 0; i < names.Length; i++)
            {
                var name = names[i];
                var found = candidates.Where(c => c.Side == side && c.Role == role && c.Name == name).ToList();
                var ids = found.Select(c => c.Pcode).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).Distinct().Order(StringComparer.Ordinal).ToArray();
                var status = repeatedNames.Contains(name) ? "ambiguous_kbo_rows"
                    : found.Any(c => c.InvalidMetadata || (c.Pcode != null && conflictingIds.Contains(c.Pcode))) ? "identity_conflict"
                    : ids.Length == 0 ? "unmatched"
                    : ids.Length > 1 ? "ambiguous" : "matched_unique_name";
                var ok = status == "matched_unique_name";
                if (ok) matched++; else unresolved++;
                identities.Add(new(i + 1, name, ok ? ids[0] : null,
                    ok ? found.Select(c => c.Birth).FirstOrDefault(v => !string.IsNullOrEmpty(v)) : null,
                    ok ? found.Select(c => c.Number).FirstOrDefault(v => !string.IsNullOrEmpty(v)) : null,
                    status, ids));
            }
            return table with { RowIdentities = identities };
        }

        var enriched = box with
        {
            Away = box.Away with { Batting = Map(box.Away.Batting, "away", "batter", "타자"), Pitching = Map(box.Away.Pitching, "away", "pitcher", "투수") },
            Home = box.Home with { Batting = Map(box.Home.Batting, "home", "batter", "타자"), Pitching = Map(box.Home.Pitching, "home", "pitcher", "투수") }
        };
        return kbo with
        {
            SchemaVersion = 3,
            BoxScore = enriched,
            IdentityMapping = new("naver_lineup", Path.GetFileName(sourceFileName),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(naverJson))).ToLowerInvariant(),
                Str(game, "gameId")!, DateTimeOffset.UtcNow, "same_game_team_role_unique_name", matched, unresolved)
        };
    }

    public static string FindSourceFile(GameRequest game, string directory)
    {
        var files = new[] { Path.Combine(directory, game.GameId + game.Year + ".json"), Path.Combine(directory, game.GameId + ".json") }
            .Where(File.Exists).ToArray();
        return files.Length switch
        {
            1 => files[0],
            0 => throw new FileNotFoundException($"해당 경기 네이버 JSON이 없습니다: {game.GameId}"),
            _ => throw new InvalidDataException("같은 경기의 네이버 JSON이 두 개 있습니다. 하나만 있는 폴더를 지정하세요.")
        };
    }

    // Updating an existing JSON only adds identity properties; preserve unknown fields too.
    public static string EnrichJson(GameRequest expected, string kboJson, string naverJson, string sourceFileName)
    {
        var doc = JsonSerializer.Deserialize<RelayDocument>(kboJson, RelayClient.JsonOptions)
            ?? throw new InvalidDataException("KBO JSON을 읽을 수 없습니다.");
        if (doc.GameId != expected.GameId || doc.LeagueId != expected.LeagueId || doc.SeriesId != expected.SeriesId)
            throw new InvalidDataException("저장된 KBO JSON의 경기/시리즈가 파일명과 다릅니다.");
        if (doc.Logs is not { Count: > 0 }) throw new InvalidDataException("KBO 플레이로그가 없습니다.");
        var enriched = Enrich(doc, naverJson, sourceFileName);
        var root = JsonNode.Parse(kboJson)!.AsObject();
        root["schemaVersion"] = 3;
        root["identityMapping"] = JsonSerializer.SerializeToNode(enriched.IdentityMapping, RelayClient.JsonOptions);
        foreach (var (side, team) in new[] { ("away", enriched.BoxScore!.Away), ("home", enriched.BoxScore.Home) })
            foreach (var (role, table) in new[] { ("batting", team.Batting), ("pitching", team.Pitching) })
                root["boxScore"]![side]![role]!["rowIdentities"] = JsonSerializer.SerializeToNode(table.RowIdentities, RelayClient.JsonOptions);
        return root.ToJsonString(RelayClient.JsonOptions);
    }
}
