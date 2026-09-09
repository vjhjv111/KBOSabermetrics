using System.Globalization;
using Microsoft.Data.Sqlite;
using NaverRelay.Application.Statistics;
using NaverRelay.Parsing;
using NaverRelay.Application.Queries;

namespace NaverRelay.Infrastructure.Sqlite;

public sealed partial class DatabaseCacheService
{
    public Task<PagedResult<PlateAppearanceGridRow>> GetPlateAppearancePageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "PlateAppearances",
            "pa.GameId IN (SELECT GameId FROM FilteredGames)",
            "pa.GameId, pa.SequenceNumber",
            """
                pa.GameId, pa.SequenceNumber, pa.OfficialSequenceNumber, pa.Inning, pa.BattingSide,
                pa.BattingTeamCode, pa.BatterPcode, pa.BatterName, pa.PitcherPcode, pa.PitcherName,
                pa.ResultType, pa.ResultText, pa.IsOfficial, pa.CountsAsAtBat, pa.RunsScored,
                pa.OutsRecorded, pa.ActualPitchCount, pa.WpaByPlate
                """,
            "pa",
            pageIndex,
            pageSize,
            reader => new PlateAppearanceGridRow
            {
                GameId = reader.GetString(0),
                Sequence = ReadInt32(reader, 1),
                OfficialSequence = NullableInt(reader, 2),
                Inning = DisplayText.Inning(NullableInt(reader, 3), (TeamSide)ReadInt32(reader, 4)),
                TeamCode = NullableString(reader, 5),
                BatterPcode = NullableString(reader, 6), Batter = NullableString(reader, 7),
                PitcherPcode = NullableString(reader, 8), Pitcher = NullableString(reader, 9),
                Outcome = DisplayText.BattingResult((BattingResultType)ReadInt32(reader, 10)),
                ResultText = NullableString(reader, 11),
                IsOfficial = DisplayText.YesNo(ReadInt32(reader, 12) != 0),
                CountsAsAtBat = DisplayText.YesNo(ReadInt32(reader, 13) != 0),
                Runs = ReadInt32(reader, 14), Outs = ReadInt32(reader, 15),
                Pitches = ReadInt32(reader, 16), Wpa = NullableDouble(reader, 17),
            },
            cancellationToken);

    public Task<PagedResult<PitchGridRow>> GetPitchPageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "Pitches",
            "p.GameId IN (SELECT GameId FROM FilteredGames)",
            "p.GameId, p.ActualPitchIndex",
            """
                p.GameId, p.Inning, p.BattingSide, p.ActualPitchIndex, p.DisplayPitchNumber,
                p.PitcherPcode, p.PitcherName, p.BatterPcode, p.BatterName,
                p.BallsBefore, p.StrikesBefore, p.PitchResult, p.PitchType, p.SpeedKmh,
                COALESCE(p.CalculatedCrossPlateX, p.CrossPlateX), p.CalculatedCrossPlateZ,
                p.IsInNominalStrikeZone, p.HasPtsTracking, p.IsSwing, p.IsWhiff, p.IsInPlay
                """,
            "p",
            pageIndex,
            pageSize,
            reader => new PitchGridRow
            {
                GameId = reader.GetString(0),
                Inning = DisplayText.Inning(NullableInt(reader, 1), (TeamSide)ReadInt32(reader, 2)),
                ActualPitchIndex = ReadInt32(reader, 3),
                DisplayPitchNumber = NullableInt(reader, 4),
                PitcherPcode = NullableString(reader, 5), Pitcher = NullableString(reader, 6),
                BatterPcode = NullableString(reader, 7), Batter = NullableString(reader, 8),
                CountBefore = $"{NullableInt(reader, 9)?.ToString(CultureInfo.InvariantCulture) ?? "-"}-{NullableInt(reader, 10)?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
                Result = DisplayText.PitchResult((PitchResultType)ReadInt32(reader, 11)),
                PitchType = NullableString(reader, 12), SpeedKmh = NullableDouble(reader, 13),
                PlateX = NullableDouble(reader, 14), PlateZ = NullableDouble(reader, 15),
                InZone = DisplayText.NullableYesNo(NullableBoolean(reader, 16)),
                HasPts = DisplayText.YesNo(ReadInt32(reader, 17) != 0),
                IsSwing = DisplayText.YesNo(ReadInt32(reader, 18) != 0),
                IsWhiff = DisplayText.YesNo(ReadInt32(reader, 19) != 0),
                IsInPlay = DisplayText.YesNo(ReadInt32(reader, 20) != 0),
            },
            cancellationToken);

    public Task<PagedResult<RunnerGridRow>> GetRunnerPageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "RunnerEvents",
            "r.GameId IN (SELECT GameId FROM FilteredGames)",
            "r.GameId, r.SourceRelayNo, r.SourceOptionIndex",
            """
                r.GameId, r.Inning, r.BattingSide, r.TeamCode, r.RunnerPcode, r.RunnerName,
                r.FromBase, r.ToBase, r.EventType, r.Reason, r.IsOut, r.IsRun, r.WasParsed, r.RawText
                """,
            "r",
            pageIndex,
            pageSize,
            reader => new RunnerGridRow
            {
                GameId = reader.GetString(0),
                Inning = DisplayText.Inning(NullableInt(reader, 1), (TeamSide)ReadInt32(reader, 2)),
                TeamCode = NullableString(reader, 3), RunnerPcode = NullableString(reader, 4),
                Runner = NullableString(reader, 5), FromBase = DisplayText.Base(NullableInt(reader, 6)),
                ToBase = DisplayText.Base(NullableInt(reader, 7)),
                EventType = DisplayText.RunnerEvent((RunnerEventType)ReadInt32(reader, 8)),
                Reason = DisplayText.RunnerReason((RunnerAdvanceReason)ReadInt32(reader, 9)),
                IsOut = DisplayText.YesNo(ReadInt32(reader, 10) != 0),
                IsRun = DisplayText.YesNo(ReadInt32(reader, 11) != 0),
                WasParsed = DisplayText.YesNo(ReadInt32(reader, 12) != 0),
                RawText = NullableString(reader, 13),
            },
            cancellationToken);

    public Task<PagedResult<PlayerChangeGridRow>> GetPlayerChangePageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "PlayerChanges",
            "c.GameId IN (SELECT GameId FROM FilteredGames)",
            "c.GameId, c.SourceRelayNo, c.SourceOptionIndex",
            """
                c.GameId, c.Inning, c.BattingSide, c.TeamCode, c.ChangeType,
                c.OutPlayerName, c.OutPosition, c.InPlayerName, c.InPosition, c.BatOrder,
                c.IsPitcherChange, c.IsPinchHitter, c.IsPinchRunner, c.WasParsed, c.RawText
                """,
            "c",
            pageIndex,
            pageSize,
            reader => new PlayerChangeGridRow
            {
                GameId = reader.GetString(0),
                Inning = DisplayText.Inning(NullableInt(reader, 1), (TeamSide)ReadInt32(reader, 2)),
                TeamCode = NullableString(reader, 3),
                ChangeType = ((PlayerChangeType)ReadInt32(reader, 4)).ToString(),
                OutPlayer = NullableString(reader, 5), OutPosition = NullableString(reader, 6),
                InPlayer = NullableString(reader, 7), InPosition = NullableString(reader, 8),
                BatOrder = NullableInt(reader, 9),
                IsPitcherChange = DisplayText.YesNo(ReadInt32(reader, 10) != 0),
                IsPinchHitter = DisplayText.YesNo(ReadInt32(reader, 11) != 0),
                IsPinchRunner = DisplayText.YesNo(ReadInt32(reader, 12) != 0),
                WasParsed = DisplayText.YesNo(ReadInt32(reader, 13) != 0),
                RawText = NullableString(reader, 14),
            },
            cancellationToken);

    public Task<PagedResult<AdministrativeGridRow>> GetAdministrativePageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "AdministrativeEvents",
            "a.GameId IN (SELECT GameId FROM FilteredGames)",
            "a.GameId, a.SourceRelayNo, a.SourceOptionIndex",
            """
                a.GameId, a.Inning, a.BattingSide, a.EventType, a.AutomaticBallDelta,
                a.AutomaticStrikeDelta, a.ReviewOverturned, a.RawText
                """,
            "a",
            pageIndex,
            pageSize,
            reader => new AdministrativeGridRow
            {
                GameId = reader.GetString(0),
                Inning = DisplayText.Inning(NullableInt(reader, 1), (TeamSide)ReadInt32(reader, 2)),
                BattingSide = DisplayText.TeamSide((TeamSide)ReadInt32(reader, 2)),
                EventType = DisplayText.Administrative((AdministrativeEventType)ReadInt32(reader, 3)),
                AutomaticBallDelta = ReadInt32(reader, 4),
                AutomaticStrikeDelta = ReadInt32(reader, 5),
                ReviewOverturned = DisplayText.NullableYesNo(NullableBoolean(reader, 6)),
                RawText = NullableString(reader, 7),
            },
            cancellationToken);

    public Task<PagedResult<DiagnosticGridRow>> GetDiagnosticPageAsync(
        GameQuery query, int pageIndex, int pageSize, CancellationToken cancellationToken = default) =>
        ReadRawPageAsync(
            query,
            "Diagnostics",
            "d.GameId IN (SELECT GameId FROM FilteredGames)",
            "d.GameId, d.DiagnosticId",
            """
                d.Severity, d.GameId, d.Code, d.Message, d.SourceRelayNo,
                d.SourceSeqNo, d.EventId
                """,
            "d",
            pageIndex,
            pageSize,
            reader => new DiagnosticGridRow
            {
                Severity = DisplayText.Severity((DiagnosticSeverity)ReadInt32(reader, 0)),
                GameId = NullableString(reader, 1), Code = reader.GetString(2),
                Message = reader.GetString(3), RelayNo = NullableInt(reader, 4),
                SeqNo = NullableInt(reader, 5), EventId = NullableString(reader, 6),
            },
            cancellationToken);

    private async Task<PagedResult<T>> ReadRawPageAsync<T>(
        GameQuery query,
        string table,
        string rowFilter,
        string orderBy,
        string selectColumns,
        string alias,
        int pageIndex,
        int pageSize,
        Func<SqliteDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var filter = BuildFilteredGamesCte(query);
        var safePageSize = Math.Clamp(pageSize, 1, 10000);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        long total;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = $"{filter.Cte} SELECT COUNT(*) FROM {table} {alias} WHERE {rowFilter};";
            AddParameters(countCommand, filter.Parameters);
            total = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }

        var pageCount = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)safePageSize);
        var safePageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, pageCount - 1));
        var rows = new List<T>(safePageSize);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                {filter.Cte}
                SELECT {selectColumns}
                FROM {table} {alias}
                WHERE {rowFilter}
                ORDER BY {orderBy}
                LIMIT $limit OFFSET $offset;
                """;
            AddParameters(command, filter.Parameters);
            command.Parameters.AddWithValue("$limit", safePageSize);
            command.Parameters.AddWithValue("$offset", safePageIndex * safePageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) rows.Add(map(reader));
        }

        return new PagedResult<T>(rows, total, safePageIndex, safePageSize);
    }

    private static int ReadInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static double ReadDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0.0 : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
