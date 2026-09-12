using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaverRelay.Parsing;

/// <summary>KBO text is authoritative for outcomes; tracking and player IDs remain from Naver.</summary>
public static partial class KboPlayLog
{
    public sealed record Entry(int Sequence, int Inning, TeamSide Side, int Order, string Name, string Text, string? Pitcher);
    public sealed record Document(string GameId, string Json, IReadOnlyList<Entry> Entries)
    {
        public DateTimeOffset? DownloadedAt { get; init; }
        public KboBoxScore.Document? BoxScore { get; init; }
    }

    public static bool IsPlayLog(string json)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty("logs", out _) && d.RootElement.TryGetProperty("gameId", out _);
    }

    public static Document Parse(string json)
    {
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;
        if (root.TryGetProperty("schemaVersion", out var schema) && (!schema.TryGetInt32(out var v) || v is < 1 or > 3))
            throw new InvalidDataException("지원하지 않는 KBO JSON 버전입니다.");
        if (root.TryGetProperty("isComplete", out var complete) && complete.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("KBO 중계 수집이 완료되지 않았습니다.");
        var id = root.GetProperty("gameId").GetString() ?? "";
        if (!Regex.IsMatch(id, @"^\d{8}[A-Z]{4}\d$")) throw new InvalidDataException("KBO 경기 ID 형식을 확인해주세요.");
        if (root.GetProperty("order").GetString() != "chronological") throw new InvalidDataException("KBO 중계는 시간순이어야 합니다.");
        var logs = root.GetProperty("logs").EnumerateArray().ToArray();
        if (logs.Length != root.GetProperty("count").GetInt32() || logs.Length == 0 || logs[^1].GetProperty("text").GetString()?.Trim() != "경기종료")
            throw new InvalidDataException("KBO 중계가 완전하지 않습니다. 경기종료까지 수집한 파일이 필요합니다.");
        var entries = new List<Entry>();
        int inning = 0, order = 0, previous = 0;
        TeamSide side = TeamSide.Unknown;
        var pitchers = new Dictionary<TeamSide, string>();
        foreach (var log in logs)
        {
            int seq = log.GetProperty("sequence").GetInt32();
            if (seq != previous + 1) throw new InvalidDataException("KBO 중계 순번이 중복되거나 빠졌습니다.");
            previous = seq;
            var text = Regex.Replace(log.GetProperty("text").GetString() ?? "", @"\s+", " ").Trim();
            var marker = Regex.Match(text, @"^(\d+)회(초|말)\s+.+?\s+공격");
            if (marker.Success) { inning = int.Parse(marker.Groups[1].Value); side = marker.Groups[2].Value == "초" ? TeamSide.Away : TeamSide.Home; order = 0; continue; }
            var start = Regex.Match(text, @"^(\d+)번타자\s+");
            if (start.Success) { order = int.Parse(start.Groups[1].Value); continue; }
            var change = Regex.Match(text, @"^투수\s+.+?\s*:\s*투수\s+(.+?)\s+\(으\)로 교체");
            if (change.Success) { pitchers[side] = change.Groups[1].Value; continue; }
            var result = Regex.Match(text, @"^([\p{L}· .'-]+?)\s*:\s*(.+)$");
            // Runner-event subjects are excluded, but a batter result can legitimately contain
            // "다른주자 수비로 출루" (a dropped third strike while another runner is played on).
            if (!result.Success || Regex.IsMatch(text, @"^(?:[123]루)?주자\s") || text.Contains("교체")) continue;
            var outcome = BatterResultClassifier.Classify(text);
            if (!outcome.WasRecognized) continue;
            if (inning == 0 || order is < 1 or > 9) throw new InvalidDataException("KBO 타석의 이닝/타순을 확인할 수 없습니다.");
            entries.Add(new(seq, inning, side, order, result.Groups[1].Value.Trim(), text, pitchers.GetValueOrDefault(side)));
        }
        if (entries.Count == 0) throw new InvalidDataException("KBO 타석 결과가 없습니다.");
        if (root.TryGetProperty("identityMapping", out var mapping) &&
            mapping.TryGetProperty("naverGameId", out var mappedId) && mappedId.GetString() != id + id[..4])
            throw new InvalidDataException("선수 연결 자료의 경기 ID가 다릅니다.");
        return new(id, json, entries)
        {
            DownloadedAt = ReadSourceTime(root, "downloadedAt"),
            BoxScore = root.TryGetProperty("boxScore", out var box) && box.ValueKind != JsonValueKind.Null ? KboBoxScore.Parse(box, id) : null
        };
    }

    internal static DateTimeOffset? ReadSourceTime(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var time)) throw new InvalidDataException("공식 자료 수집 시각이 올바르지 않습니다.");
        return time;
    }

    /// <summary>Logs and final records have separate timestamps; a logs-only import never removes a stored box score.</summary>
    public static Document SelectPreferred(Document? existing, Document incoming)
    {
        if (existing == null) return incoming;
        if (existing.GameId != incoming.GameId) throw new InvalidDataException("서로 다른 경기의 공식 자료를 병합할 수 없습니다.");
        bool Newer(DateTimeOffset? oldTime, DateTimeOffset? newTime) => newTime.HasValue ? !oldTime.HasValue || newTime >= oldTime : !oldTime.HasValue;
        var logs = Newer(existing.DownloadedAt, incoming.DownloadedAt) ? incoming : existing;
        var boxOwner = incoming.BoxScore == null ? existing : existing.BoxScore == null ? incoming :
            Newer(existing.BoxScore.DownloadedAt ?? existing.DownloadedAt, incoming.BoxScore.DownloadedAt ?? incoming.DownloadedAt) ? incoming : existing;
        var node = System.Text.Json.Nodes.JsonNode.Parse(logs.Json)!.AsObject();
        if (boxOwner.BoxScore != null)
        {
            var boxNode = System.Text.Json.Nodes.JsonNode.Parse(boxOwner.Json)!.AsObject();
            node["boxScore"] = boxNode["boxScore"]!.DeepClone();
            node.Remove("identityMapping");
            if (boxNode["identityMapping"] != null) node["identityMapping"] = boxNode["identityMapping"]!.DeepClone();
            node["schemaVersion"] = boxOwner.BoxScore.HasRowIdentities ? 3 : 2;
        }
        return Parse(node.ToJsonString());
    }

    public static void Apply(NormalizedGame game, Document doc)
    {
        if (game.GameId != doc.GameId + doc.GameId[..4]) throw new InvalidDataException("네이버/KBO 경기 ID가 일치하지 않습니다.");
        string Team(TeamSide side) => (side == TeamSide.Away ? game.AwayTeam : game.HomeTeam).TeamCode!;
        string Name(PlateAppearance p) => (p.ResultText ?? p.BatterName ?? "").Split(':')[0].Trim();
        string Key(int? inn, TeamSide side, int? order, string name) => $"{inn}:{side}:{order}:{name}";
        var existing = game.PlateAppearances.Where(p => p.IsOfficialPlateAppearance)
            .GroupBy(p => Key(p.Inning, p.BattingSide, p.BatOrder, Name(p)))
            .ToDictionary(g => g.Key, g => new Queue<PlateAppearance>(g.OrderBy(p => p.SequenceNumber)));
        var plan = new List<(Entry Entry, PlateAppearance Pa, bool Added)>();
        foreach (var e in doc.Entries)
        {
            var key = Key(e.Inning, e.Side, e.Order, e.Name);
            if (existing.TryGetValue(key, out var queue) && queue.Count > 0) { plan.Add((e, queue.Dequeue(), false)); continue; }
            var batters = game.BattingLines.Where(b => b.TeamCode == Team(e.Side) && b.Name == e.Name).ToArray();
            var fielding = Team(e.Side == TeamSide.Away ? TeamSide.Home : TeamSide.Away);
            var pitchers = game.PitchingLines.Where(p => p.TeamCode == fielding && (e.Pitcher == null ? p.AppearanceSequence == 1 : p.Name == e.Pitcher)).ToArray();
            if (batters.Length != 1 || pitchers.Length != 1) throw new InvalidDataException($"KBO {e.Inning}회 {e.Name}: 누락 타석 선수/투수 식별이 모호합니다.");
            var id = $"{game.GameId}:kbo:{e.Sequence}";
            plan.Add((e, new PlateAppearance { GameId = game.GameId, PlateAppearanceId = id + ":pa", RelayGroupId = id + ":relay",
                Inning = e.Inning, BattingSide = e.Side, RawHomeOrAway = e.Side == TeamSide.Away ? "0" : "1",
                BattingTeamCode = Team(e.Side), FieldingTeamCode = fielding, BatOrder = e.Order,
                BatterName = e.Name, BatterPcode = batters[0].Pcode, PitcherName = pitchers[0].Name, PitcherPcode = pitchers[0].Pcode,
                FinalPitcherName = pitchers[0].Name, FinalPitcherPcode = pitchers[0].Pcode, Status = PlateAppearanceStatus.Completed,
                IsOfficialPlateAppearance = true, ResultEventId = id + ":event" }, true));
        }
        if (existing.Values.Any(q => q.Count != 0)) throw new InvalidDataException("KBO 미매칭 타석: " + string.Join("; ", existing.Values.SelectMany(q => q).Select(p => $"{p.Inning}회 {p.BatOrder}번 {p.ResultText}")));
        // Match repeated appearances only when their counts are equal; never guess which occurrence is missing.
        foreach (var g in plan.GroupBy(x => Key(x.Entry.Inning, x.Entry.Side, x.Entry.Order, x.Entry.Name)))
            if (g.Count() > 1 && g.Any(x => x.Added)) throw new InvalidDataException("한 이닝 동일 선수 복수 타석의 누락 위치를 확정할 수 없습니다.");
        foreach (var (e, pa, added) in plan)
        {
            pa.Outcome = BatterResultClassifier.Classify(e.Text);
            pa.ResultText = e.Text;
            if (added)
            {
                // No synthetic tracking, state, WPA or pitch measurements.
                pa.OutsRecorded = pa.Outcome.IsOut ? 1 : 0;
                game.PlateAppearances.Add(pa);
                game.RelayGroups.Add(new RelayGroup { GameId = game.GameId, RelayGroupId = pa.RelayGroupId, Inning = e.Inning,
                    BattingSide = e.Side, BattingTeamCode = pa.BattingTeamCode, PlateAppearanceId = pa.PlateAppearanceId,
                    GroupType = RelayGroupType.CompletedPlateAppearance, Title = "KBO 보완: " + e.Name });
                game.Events.Add(new NormalizedEvent { GameId = game.GameId, EventId = pa.ResultEventId!, RelayGroupId = pa.RelayGroupId,
                    PlateAppearanceId = pa.PlateAppearanceId, Inning = e.Inning, BattingSide = e.Side,
                    EventType = NormalizedEventType.BatterResult, RawText = e.Text, ChronologicalIndex = e.Sequence });
            }
        }
        int index = 0;
        foreach (var item in plan) { item.Pa.SequenceNumber = index; item.Pa.OfficialSequenceNumber = ++index; }
        game.Summary.CompletedPlateAppearanceCount = plan.Count;
        foreach (var old in game.Diagnostics.Where(d => d.Code.StartsWith("FINAL_LINE_")))
        {
            old.Code = "NAVER_SOURCE_" + old.Code;
            old.Message = "공식 중계 보정 전 네이버 원본 대조: " + old.Message;
            old.Severity = DiagnosticSeverity.Info;
        }
        game.Summary.FinalLineBattingMismatchCount = 0;
        game.Diagnostics.Add(new ParserDiagnostic { GameId = game.GameId, Severity = DiagnosticSeverity.Info, Code = "KBO_PLAYLOG_APPLIED",
            Message = $"공식 문자중계 {plan.Count}타석 대조, {plan.Count(x => x.Added)}타석 보완. 기존 투구 데이터 보존; 보완 타석의 투구/WPA는 미제공." });
        ReconcileLines(game);
        game.Summary.WarningCount = game.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        game.Summary.ErrorCount = game.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    }

    public static void ReconcileLines(NormalizedGame game)
    {
        var pas = game.PlateAppearances.Where(p => p.IsOfficialPlateAppearance).ToArray();
        foreach (var b in game.BattingLines)
        {
            var rows = pas.Where(p => p.BatterPcode == b.Pcode && p.BattingTeamCode == b.TeamCode).ToArray();
            b.PlateAppearances = rows.Length; b.AtBats = rows.Count(p => p.Outcome.CountsAsAtBat);
            b.Hits = rows.Count(p => p.Outcome.IsHit); b.HomeRuns = rows.Count(p => p.Outcome.ResultType == BattingResultType.HomeRun);
            b.Walks = rows.Count(p => p.Outcome.IsWalk); b.Strikeouts = rows.Count(p => p.Outcome.IsStrikeout);
            b.HitByPitch = rows.Count(p => p.Outcome.ResultType == BattingResultType.HitByPitch);
        }
        foreach (var p in game.PitchingLines)
        {
            var rows = pas.Where(pa => pa.PitcherPcode == p.Pcode && pa.FieldingTeamCode == p.TeamCode).ToArray();
            p.HitsAllowed = rows.Count(pa => pa.Outcome.IsHit); p.HomeRunsAllowed = rows.Count(pa => pa.Outcome.ResultType == BattingResultType.HomeRun);
            p.Walks = rows.Count(pa => pa.Outcome.IsWalk); p.Strikeouts = rows.Count(pa => pa.Outcome.IsStrikeout);
            p.HitBatters = rows.Count(pa => pa.Outcome.ResultType == BattingResultType.HitByPitch);
        }
    }
}
