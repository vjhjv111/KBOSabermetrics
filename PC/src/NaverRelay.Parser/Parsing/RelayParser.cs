using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    /// <summary>
    /// Converts Naver's display-oriented relay structure into normalized game, PA, pitch,
    /// runner, substitution, and administrative-event records.
    /// </summary>
    public static class RelayParser
    {
        public static NormalizedGame Parse(NaverRelayResponse response)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            var relayData = response.Result?.TextRelayData;
            var game = response.Result?.Game;
            var normalized = CreateGameMetadata(game, relayData);

            if (relayData == null)
            {
                normalized.Diagnostics.Add(new ParserDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Code = "MISSING_TEXT_RELAY_DATA",
                    Message = "The response does not contain result.textRelayData.",
                    GameId = normalized.GameId,
                });
                FinalizeSummary(normalized, Array.Empty<TextRelayPlay>(), new Dictionary<int, int>());
                return normalized;
            }

            var context = new ParsingContext(game, relayData);
            normalized.GameId = context.GameId;
            normalized.LastValidHomeWinRate = relayData.LastValidMetricOption?.HomeTeamWinRate;
            normalized.LastValidAwayWinRate = relayData.LastValidMetricOption?.AwayTeamWinRate;
            normalized.LastValidWpaByPlate = relayData.LastValidMetricOption?.WpaByPlate;
            ExtractFinalLines(normalized, relayData, context);

            var sourcePlays = relayData.TextRelays ?? new List<TextRelayPlay>();
            var chronologicalPlays = RemoveReissuedSegments(OrderChronologically(sourcePlays), normalized);
            var seqNoCounts = CountSourceSeqNos(chronologicalPlays);
            var duplicateSeqNos = seqNoCounts.Where(pair => pair.Value > 1).ToDictionary(pair => pair.Key, pair => pair.Value);

            CurrentGameState? previousRawState = null;
            var previousBattingSide = TeamSide.Unknown;
            var globalEventIndex = 0;
            var plateAppearanceSequence = 0;
            var officialPlateAppearanceSequence = 0;

            for (var relayIndex = 0; relayIndex < chronologicalPlays.Count; relayIndex++)
            {
                var play = chronologicalPlays[relayIndex];
                var options = play.TextOptions ?? new List<TextOption>();
                var battingSide = ParserUtilities.ParseBattingSide(play.HomeOrAway);
                var groupType = ClassifyGroup(play);
                var createPlateAppearance = ShouldCreatePlateAppearance(groupType, options);
                var plateAppearanceId = createPlateAppearance
                    ? ParserUtilities.PlateAppearanceId(context.GameId, play.No, relayIndex)
                    : null;
                var relayGroupId = ParserUtilities.RelayGroupId(context.GameId, play.No, relayIndex);

                var group = new RelayGroup
                {
                    RelayGroupId = relayGroupId,
                    GameId = context.GameId,
                    ChronologicalIndex = relayIndex + 1,
                    SourceRelayNo = play.No,
                    Title = play.Title,
                    TitleStyle = play.TitleStyle,
                    Inning = play.Inn,
                    RawHomeOrAway = play.HomeOrAway,
                    BattingSide = battingSide,
                    BattingTeamCode = context.GetTeamCode(battingSide),
                    SourceStatusCode = play.StatusCode,
                    GroupType = groupType,
                    PlateAppearanceId = plateAppearanceId,
                    HomeWinRateAfter = play.MetricOption?.HomeTeamWinRate,
                    AwayWinRateAfter = play.MetricOption?.AwayTeamWinRate,
                    WpaByPlate = play.MetricOption?.WpaByPlate,
                };

                var eventPairs = new List<(TextOption Raw, NormalizedEvent Normalized)>();
                var groupPitches = new List<PitchEvent>();
                var groupRunners = new List<RunnerEvent>();
                var groupChanges = new List<PlayerChangeEvent>();
                var groupAdministrative = new List<AdministrativeEvent>();
                var actualPitchIndex = 0;
                var ptsById = BuildPtsLookup(play.PtsOptions);

                for (var optionIndex = 0; optionIndex < options.Count; optionIndex++)
                {
                    var option = options[optionIndex];
                    var rawBefore = previousRawState ?? option.CurrentGameState;
                    var beforeSide = previousRawState == null ? battingSide : previousBattingSide;
                    var beforeState = context.NormalizeState(rawBefore, beforeSide);
                    var eventId = ParserUtilities.EventId(context.GameId, play.No, relayIndex, optionIndex);
                    var sourceSeqNo = option.Seqno;
                    var normalizedEvent = new NormalizedEvent
                    {
                        EventId = eventId,
                        GameId = context.GameId,
                        RelayGroupId = relayGroupId,
                        PlateAppearanceId = plateAppearanceId,
                        ChronologicalIndex = ++globalEventIndex,
                        SourceRelayNo = play.No,
                        SourceOptionIndex = optionIndex,
                        SourceSeqNo = sourceSeqNo,
                        IsDuplicateSourceSeqNo = sourceSeqNo.HasValue
                            && duplicateSeqNos.ContainsKey(sourceSeqNo.Value),
                        RawType = option.Type,
                        EventType = ParserUtilities.MapEventType(option.Type),
                        RawText = option.Text,
                        RawStuff = option.Stuff,
                        BattingSide = battingSide,
                        Inning = play.Inn,
                        StateBefore = beforeState,
                    };

                    PlayerChangeEvent? playerChange = null;
                    if (normalizedEvent.EventType == NormalizedEventType.PlayerChange)
                    {
                        playerChange = PlayerChangeParser.Parse(option, normalizedEvent, context);
                        if (playerChange.WasParsed)
                        {
                            context.ApplyPlayerChange(playerChange);
                        }
                    }

                    var rawAfter = option.CurrentGameState ?? rawBefore;
                    var afterState = context.NormalizeState(rawAfter, battingSide);
                    normalizedEvent.StateAfter = afterState;
                    normalized.Events.Add(normalizedEvent);
                    group.EventIds.Add(normalizedEvent.EventId);
                    eventPairs.Add((option, normalizedEvent));

                    switch (normalizedEvent.EventType)
                    {
                        case NormalizedEventType.Pitch:
                            actualPitchIndex++;
                            var pitch = CreatePitch(
                                option,
                                normalizedEvent,
                                actualPitchIndex,
                                ptsById,
                                context,
                                normalized.Diagnostics);
                            normalized.PitchEvents.Add(pitch);
                            groupPitches.Add(pitch);
                            break;

                        case NormalizedEventType.RunnerResult:
                            var runner = RunnerEventParser.Parse(option, normalizedEvent, context);
                            runner.StateAfter = afterState;
                            normalized.RunnerEvents.Add(runner);
                            groupRunners.Add(runner);
                            if (!runner.WasParsed)
                            {
                                AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNPARSED_RUNNER_EVENT",
                                    $"Runner event could not be parsed: {option.Text}", group, normalizedEvent);
                            }
                            break;

                        case NormalizedEventType.PlayerChange:
                            if (playerChange != null)
                            {
                                playerChange.StateAfter = afterState;
                                normalized.PlayerChanges.Add(playerChange);
                                groupChanges.Add(playerChange);
                                if (!playerChange.WasParsed)
                                {
                                    AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNPARSED_PLAYER_CHANGE",
                                        $"Player-change event could not be parsed: {option.Text}", group, normalizedEvent);
                                }
                            }
                            break;

                        case NormalizedEventType.Administrative:
                            var administrative = AdministrativeEventParser.Parse(option, normalizedEvent);
                            administrative.StateAfter = afterState;
                            normalized.AdministrativeEvents.Add(administrative);
                            groupAdministrative.Add(administrative);
                            if (!administrative.WasRecognized)
                            {
                                AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNKNOWN_ADMIN_EVENT",
                                    $"Administrative event was preserved but not classified: {option.Text}", group, normalizedEvent);
                            }
                            break;

                        case NormalizedEventType.Unknown:
                            AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNKNOWN_RAW_EVENT_TYPE",
                                $"Unknown raw event type {option.Type?.ToString() ?? "null"}: {option.Text}",
                                group, normalizedEvent);
                            break;
                    }

                    if (option.BatterRecord != null && IsCompletelyEmpty(option.BatterRecord))
                    {
                        AddDiagnostic(normalized, DiagnosticSeverity.Info, "EMPTY_BATTER_RECORD",
                            "A batterRecord object was present, but all identifying/stat fields were null.",
                            group, normalizedEvent);
                    }

                    previousRawState = rawAfter;
                    previousBattingSide = battingSide;
                }

                group.StateBefore = eventPairs.Count > 0 ? eventPairs[0].Normalized.StateBefore : null;
                group.StateAfter = eventPairs.Count > 0 ? eventPairs[^1].Normalized.StateAfter : null;
                normalized.RelayGroups.Add(group);

                if (groupType == RelayGroupType.Unknown)
                {
                    var rawTypes = string.Join(",", options.Select(option => option.Type?.ToString() ?? "null"));
                    AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNKNOWN_RELAY_GROUP",
                        $"Relay group did not match a known shape. Raw types: [{rawTypes}]", group,
                        eventPairs.Count > 0 ? eventPairs[^1].Normalized : null);
                }

                if (createPlateAppearance)
                {
                    plateAppearanceSequence++;
                    var isOfficial = groupType == RelayGroupType.CompletedPlateAppearance;
                    if (isOfficial)
                    {
                        officialPlateAppearanceSequence++;
                    }

                    var plateAppearance = BuildPlateAppearance(
                        play,
                        group,
                        eventPairs,
                        groupPitches,
                        groupRunners,
                        groupChanges,
                        groupAdministrative,
                        context,
                        plateAppearanceSequence,
                        isOfficial ? officialPlateAppearanceSequence : null);
                    normalized.PlateAppearances.Add(plateAppearance);

                    if (plateAppearance.Status != PlateAppearanceStatus.Completed)
                    {
                        AddDiagnostic(normalized, DiagnosticSeverity.Info, "NON_OFFICIAL_PLATE_APPEARANCE",
                            $"PA sequence was preserved as {plateAppearance.Status}; it does not count as an official PA.",
                            group, eventPairs.Count > 0 ? eventPairs[^1].Normalized : null);
                    }
                    else if (!plateAppearance.Outcome.WasRecognized)
                    {
                        AddDiagnostic(normalized, DiagnosticSeverity.Warning, "UNKNOWN_BATTER_RESULT",
                            $"Batter result was preserved but not classified: {plateAppearance.ResultText}",
                            group, FindEvent(eventPairs, plateAppearance.ResultEventId));
                    }
                }
            }

            normalized.FinalRelayState = context.NormalizeState(
                relayData.CurrentGameState,
                ParserUtilities.ParseBattingSide(relayData.HomeOrAway));
            AddDuplicateSeqNoDiagnostics(normalized, duplicateSeqNos);
            for(int i=1;i<normalized.PlateAppearances.Count;i++)
            {
                var pa=normalized.PlateAppearances[i];var prior=normalized.PlateAppearances[i-1];
                if(pa.Outcome.IsStrikeout&&prior.Status==PlateAppearanceStatus.IncompleteUnknown&&prior.StateAfter?.Strikes==2&&prior.Inning==pa.Inning&&prior.BattingTeamCode==pa.BattingTeamCode&&prior.BatOrder==pa.BatOrder&&prior.BatterPcode!=pa.BatterPcode)
                {
                    normalized.Diagnostics.Add(new ParserDiagnostic{GameId=normalized.GameId,Severity=DiagnosticSeverity.Info,Code="TWO_STRIKE_SUBSTITUTION",Message=$"삼진 귀속: {pa.BatterName} → {prior.BatterName} (2스트라이크 교체)"});
                    pa.BatterPcode=prior.BatterPcode;pa.BatterName=prior.BatterName;
                }
            }
            GameConsistencyValidator.Validate(normalized, relayData);
            FinalizeSummary(normalized, chronologicalPlays, seqNoCounts);
            return normalized;
        }

        public static NormalizedGame ParseJson(string json)
        {
            var input = RelayInput.Read(json);
            if (input.NaverJson == null) throw new InvalidDataException("공식 단독 JSON은 기존 네이버 경기와 함께 수집해주세요.");
            var response = RelayDeserializer.Deserialize(input.NaverJson)
                ?? throw new InvalidDataException("The JSON did not contain a relay response.");
            if (response.Result?.TextRelayData == null) throw new InvalidDataException("result.textRelayData가 없는 JSON입니다.");
            var parsed = Parse(response);
            if (string.IsNullOrWhiteSpace(parsed.GameId)) throw new InvalidDataException("경기 ID가 없는 JSON은 저장할 수 없습니다.");
            parsed.ImportedOfficialSource = input.Official;
            parsed.ImportedSourceJson = json;
            if (input.Official != null && parsed.GameId != input.Official.GameId + input.Official.GameId[..4])
                throw new InvalidDataException("통합 JSON의 네이버/공식 경기 ID가 다릅니다.");
            return parsed;
        }

        public static NormalizedGame ParseFile(string filePath)
        {
            return ParseJson(File.ReadAllText(filePath));
        }

        /// <summary>Compatibility helper for the original prototype call site.</summary>
        public static (List<PlateAppearance> plateAppearances, List<PitchEvent> pitchEvents) Flatten(
            NaverRelayResponse response)
        {
            var parsed = Parse(response);
            return (parsed.PlateAppearances, parsed.PitchEvents);
        }

        private static RelayGroupType ClassifyGroup(TextRelayPlay play)
        {
            var options = play.TextOptions ?? new List<TextOption>();
            var types = options.Select(o => o.Type).ToList();
            var hasStart = types.Contains(8);
            var hasPitch = types.Contains(1);
            var hasBatterResult = types.Any(t => t == 13 || t == 23);
            var hasRunnerResult = types.Any(t => t == 14 || t == 24);
            var hasPlayerChange = types.Contains(2);

            if (types.Count > 0 && types.All(t => t == 0))
            {
                return RelayGroupType.InningMarker;
            }

            if (types.Count > 0 && types.All(t => t == 99))
            {
                return RelayGroupType.GameSummary;
            }

            if (hasBatterResult)
            {
                return RelayGroupType.CompletedPlateAppearance;
            }

            if (hasStart && hasPitch && hasRunnerResult
                && ParserUtilities.ParseInt(options.LastOrDefault()?.CurrentGameState?.Out) == 3)
            {
                return RelayGroupType.InterruptedPlateAppearance;
            }

            if (hasStart && !hasPitch && hasPlayerChange)
            {
                return RelayGroupType.PrePlateSubstitution;
            }

            if (hasStart && (hasPitch || hasRunnerResult || types.Contains(7)))
            {
                return RelayGroupType.IncompletePlateAppearance;
            }

            if (types.Count > 0 && types.All(t => t == 2))
            {
                return RelayGroupType.PlayerChangeOnly;
            }

            if (types.Count > 0 && types.All(t => t == 7))
            {
                return RelayGroupType.AdministrativeOnly;
            }

            return RelayGroupType.Unknown;
        }

        private static bool ShouldCreatePlateAppearance(RelayGroupType groupType, List<TextOption> options)
        {
            return groupType == RelayGroupType.CompletedPlateAppearance
                || groupType == RelayGroupType.InterruptedPlateAppearance
                || (groupType == RelayGroupType.IncompletePlateAppearance && options.Any(o => o.Type == 1));
        }

        private static PlateAppearance BuildPlateAppearance(
            TextRelayPlay play,
            RelayGroup group,
            List<(TextOption Raw, NormalizedEvent Normalized)> eventPairs,
            List<PitchEvent> pitches,
            List<RunnerEvent> runners,
            List<PlayerChangeEvent> changes,
            List<AdministrativeEvent> administrativeEvents,
            ParsingContext context,
            int sequenceNumber,
            int? officialSequenceNumber)
        {
            var startPair = eventPairs.FirstOrDefault(pair => pair.Raw.Type == 8);
            var resultPair = eventPairs.FirstOrDefault(pair => pair.Raw.Type == 13 || pair.Raw.Type == 23);
            var firstPitch = pitches.FirstOrDefault();
            var lastEvent = eventPairs.LastOrDefault().Normalized;
            var status = group.GroupType switch
            {
                RelayGroupType.CompletedPlateAppearance => PlateAppearanceStatus.Completed,
                RelayGroupType.InterruptedPlateAppearance => PlateAppearanceStatus.InterruptedByRunnerOut,
                _ => PlateAppearanceStatus.IncompleteUnknown,
            };

            var batterPcode = firstPitch?.BatterPcode
                ?? startPair.Raw?.BatterRecord?.Pcode
                ?? startPair.Normalized?.StateAfter?.BatterPcode
                ?? resultPair.Normalized?.StateAfter?.BatterPcode;
            var batter = context.FindByPcode(batterPcode)
                ?? context.FindByName(startPair.Raw?.BatterRecord?.Name, group.BattingSide);
            var pitcherPcode = firstPitch?.PitcherPcode
                ?? resultPair.Normalized?.StateAfter?.PitcherPcode
                ?? startPair.Normalized?.StateAfter?.PitcherPcode;
            var finalPitcherPcode = resultPair.Normalized?.StateAfter?.PitcherPcode
                ?? lastEvent?.StateAfter?.PitcherPcode
                ?? pitcherPcode;
            var pitcher = context.FindByPcode(pitcherPcode);
            var finalPitcher = context.FindByPcode(finalPitcherPcode);
            var stateBefore = startPair.Normalized?.StateAfter ?? group.StateBefore;
            var stateAfter = group.StateAfter;
            var outcome = status == PlateAppearanceStatus.Completed
                ? BatterResultClassifier.Classify(resultPair.Raw?.Text)
                : new BattingOutcome();

            var runsScored = group.BattingSide switch
            {
                TeamSide.Away => ParserUtilities.PositiveDifference(stateAfter?.AwayScore, stateBefore?.AwayScore),
                TeamSide.Home => ParserUtilities.PositiveDifference(stateAfter?.HomeScore, stateBefore?.HomeScore),
                _ => 0,
            };

            return new PlateAppearance
            {
                PlateAppearanceId = group.PlateAppearanceId ?? string.Empty,
                GameId = group.GameId,
                RelayGroupId = group.RelayGroupId,
                SequenceNumber = sequenceNumber,
                OfficialSequenceNumber = officialSequenceNumber,
                SourceRelayNo = play.No,
                Inning = play.Inn,
                RawHomeOrAway = play.HomeOrAway,
                BattingSide = group.BattingSide,
                BattingTeamCode = context.GetTeamCode(group.BattingSide),
                FieldingTeamCode = context.GetTeamCode(ParserUtilities.Opposite(group.BattingSide)),
                Status = status,
                IsOfficialPlateAppearance = status == PlateAppearanceStatus.Completed,
                StartEventId = startPair.Normalized?.EventId,
                ResultEventId = resultPair.Normalized?.EventId,
                BatterPcode = batterPcode,
                BatterName = batter?.Name ?? startPair.Raw?.BatterRecord?.Name ?? stateBefore?.BatterName,
                BatOrder = batter?.BatOrder ?? startPair.Raw?.BatterRecord?.BatOrder,
                PitcherPcode = pitcherPcode,
                PitcherName = pitcher?.Name ?? firstPitch?.PitcherName,
                FinalPitcherPcode = finalPitcherPcode,
                FinalPitcherName = finalPitcher?.Name ?? pitcher?.Name ?? firstPitch?.PitcherName,
                ResultRawType = resultPair.Raw?.Type,
                ResultText = resultPair.Raw?.Text,
                Outcome = outcome,
                StateBefore = stateBefore,
                StateAfter = stateAfter,
                RunsScored = runsScored,
                OutsRecorded = ParserUtilities.PositiveDifference(stateAfter?.Outs, stateBefore?.Outs),
                ActualPitchCount = pitches.Count,
                MaximumDisplayPitchNumber = GetMaximumDisplayPitchNumber(pitches),
                HomeWinRateAfter = play.MetricOption?.HomeTeamWinRate,
                AwayWinRateAfter = play.MetricOption?.AwayTeamWinRate,
                WpaByPlate = play.MetricOption?.WpaByPlate,
                PitchEventIds = pitches.Select(p => p.PitchEventId).ToList(),
                RunnerEventIds = runners.Select(r => r.RunnerEventId).ToList(),
                PlayerChangeEventIds = changes.Select(c => c.PlayerChangeEventId).ToList(),
                AdministrativeEventIds = administrativeEvents.Select(a => a.AdministrativeEventId).ToList(),
            };
        }

        private static PitchEvent CreatePitch(
            TextOption option,
            NormalizedEvent sourceEvent,
            int actualPitchIndex,
            Dictionary<string, PtsOption> ptsById,
            ParsingContext context,
            List<ParserDiagnostic> diagnostics)
        {
            PtsOption? pts = null;
            if (!string.IsNullOrWhiteSpace(option.PtsPitchId))
            {
                ptsById.TryGetValue(option.PtsPitchId, out pts);
            }

            var pitcherPcode = sourceEvent.StateAfter?.PitcherPcode ?? sourceEvent.StateBefore?.PitcherPcode;
            var batterPcode = sourceEvent.StateAfter?.BatterPcode ?? sourceEvent.StateBefore?.BatterPcode;
            var pitch = new PitchEvent
            {
                PitchEventId = sourceEvent.EventId + ":pitch",
                SourceEventId = sourceEvent.EventId,
                GameId = sourceEvent.GameId,
                RelayGroupId = sourceEvent.RelayGroupId,
                PlateAppearanceId = sourceEvent.PlateAppearanceId,
                SourceRelayNo = sourceEvent.SourceRelayNo,
                SourceSeqNo = sourceEvent.SourceSeqNo,
                SourceOptionIndex = sourceEvent.SourceOptionIndex,
                ActualPitchIndex = actualPitchIndex,
                DisplayPitchNumber = option.PitchNum,
                Inning = sourceEvent.Inning,
                BattingSide = sourceEvent.BattingSide,
                PitcherPcode = pitcherPcode,
                PitcherName = context.FindByPcode(pitcherPcode)?.Name
                    ?? sourceEvent.StateAfter?.PitcherName
                    ?? sourceEvent.StateBefore?.PitcherName,
                BatterPcode = batterPcode,
                BatterName = context.FindByPcode(batterPcode)?.Name
                    ?? sourceEvent.StateAfter?.BatterName
                    ?? sourceEvent.StateBefore?.BatterName,
                BallsBefore = sourceEvent.StateBefore?.Balls,
                StrikesBefore = sourceEvent.StateBefore?.Strikes,
                BallsAfter = sourceEvent.StateAfter?.Balls,
                StrikesAfter = sourceEvent.StateAfter?.Strikes,
                OutsBefore = sourceEvent.StateBefore?.Outs,
                RawPitchResult = option.PitchResult,
                PitchResult = PitchResultClassifier.Parse(option.PitchResult),
                PitchType = option.Stuff,
                SpeedKmh = ParserUtilities.ParseDouble(option.Speed),
                PtsPitchId = option.PtsPitchId,
                HasPtsTracking = pts != null,
            };

            if (pts != null)
            {
                pitch.CrossPlateX = pts.CrossPlateX;
                pitch.CrossPlateY = pts.CrossPlateY;
                pitch.TopStrikeZone = pts.TopSz;
                pitch.BottomStrikeZone = pts.BottomSz;
                pitch.X0 = pts.X0;
                pitch.Y0 = pts.Y0;
                pitch.Z0 = pts.Z0;
                pitch.Vx0 = pts.Vx0;
                pitch.Vy0 = pts.Vy0;
                pitch.Vz0 = pts.Vz0;
                pitch.Ax = pts.Ax;
                pitch.Ay = pts.Ay;
                pitch.Az = pts.Az;
                pitch.BatterStance = pts.Stance;

                if (PitchTrajectoryCalculator.TryCalculateAtPlate(pts, out var time, out var x, out var z))
                {
                    pitch.TimeToPlateSeconds = time;
                    pitch.CalculatedCrossPlateX = x;
                    pitch.CalculatedCrossPlateZ = z;
                }
                else
                {
                    diagnostics.Add(new ParserDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Code = "PTS_CALCULATION_FAILED",
                        Message = $"Could not calculate plate-crossing height for PTS pitch {option.PtsPitchId}.",
                        GameId = sourceEvent.GameId,
                        RelayGroupId = sourceEvent.RelayGroupId,
                        EventId = sourceEvent.EventId,
                        SourceRelayNo = sourceEvent.SourceRelayNo,
                        SourceSeqNo = sourceEvent.SourceSeqNo,
                    });
                }

                pitch.IsInNominalStrikeZone = PitchTrajectoryCalculator.IsInNominalStrikeZone(pitch);
            }
            else
            {
                diagnostics.Add(new ParserDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "MISSING_PTS",
                    Message = $"No matching ptsOptions row for pitch id {option.PtsPitchId ?? "<null>"}.",
                    GameId = sourceEvent.GameId,
                    RelayGroupId = sourceEvent.RelayGroupId,
                    EventId = sourceEvent.EventId,
                    SourceRelayNo = sourceEvent.SourceRelayNo,
                    SourceSeqNo = sourceEvent.SourceSeqNo,
                });
            }

            if (pitch.PitchResult == PitchResultType.Unknown && !string.IsNullOrWhiteSpace(option.PitchResult))
            {
                diagnostics.Add(new ParserDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Code = "UNKNOWN_PITCH_RESULT",
                    Message = $"Unknown pitchResult code: {option.PitchResult}",
                    GameId = sourceEvent.GameId,
                    RelayGroupId = sourceEvent.RelayGroupId,
                    EventId = sourceEvent.EventId,
                    SourceRelayNo = sourceEvent.SourceRelayNo,
                    SourceSeqNo = sourceEvent.SourceSeqNo,
                });
            }

            PitchResultClassifier.SetDerivedFlags(pitch);
            return pitch;
        }

        private static int? GetMaximumDisplayPitchNumber(IEnumerable<PitchEvent> pitches)
        {
            var values = pitches
                .Where(pitch => pitch.DisplayPitchNumber.HasValue)
                .Select(pitch => pitch.DisplayPitchNumber!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Max();
        }

        private static Dictionary<string, PtsOption> BuildPtsLookup(List<PtsOption>? ptsOptions)
        {
            return (ptsOptions ?? new List<PtsOption>())
                .Where(p => !string.IsNullOrWhiteSpace(p.PitchId))
                .GroupBy(p => p.PitchId!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        private static List<TextRelayPlay> RemoveReissuedSegments(List<TextRelayPlay> plays, NormalizedGame game)
        {
            // A rewind is only accepted when the replacement repeats at least three
            // complete plate appearances with identical pitch text and starting state.
            static string? Key(TextRelayPlay p)
            {
                var start=p.TextOptions?.FirstOrDefault(o=>o.BatterRecord?.Pcode!=null);
                if(start?.CurrentGameState is null || p.TextOptions?.Any(o=>o.Type==13)!=true)return null;
                var raw=System.Text.Json.JsonSerializer.SerializeToElement(start.CurrentGameState);
                string State(string name)=>raw.TryGetProperty(name,out var v)?v.ToString():"";
                return string.Join("|",p.Inn,p.HomeOrAway,start.BatterRecord!.Pcode,
                    State("HomeScore"),State("AwayScore"),State("Out"),State("Base1"),State("Base2"),State("Base3"),
                    string.Join(";",p.TextOptions.Where(o=>o.PitchNum.HasValue||o.Type==13).Select(o=>$"{o.PitchNum}:{o.Text}")));
            }
            var result=plays.ToList();
            for(int i=1;i<result.Count;i++)
            {
                if(result[i].Inn>=result[i-1].Inn)continue;
                var key=Key(result[i]);if(key is null)continue;
                int prior=result.FindLastIndex(i-1,i,p=>Key(p)==key);if(prior<0)continue;
                var oldKeys=result.Skip(prior).Take(i-prior).Select(Key).Where(k=>k!=null).ToArray();
                var newKeys=result.Skip(i).Select(Key).Where(k=>k!=null).Take(oldKeys.Length).ToArray();
                if(oldKeys.Length<3||!oldKeys.SequenceEqual(newKeys))continue;
                game.Diagnostics.Add(new ParserDiagnostic{GameId=game.GameId,Code="REISSUED_RELAY_SEGMENT",Severity=DiagnosticSeverity.Warning,Message=$"Replaced {i-prior} earlier relay groups with a verified repeated segment."});
                result.RemoveRange(prior,i-prior);i=prior;
            }
            var seen=new HashSet<string>(StringComparer.Ordinal);
            for(int i=result.Count-1;i>=0;i--)
            {
                var key=Key(result[i]);var pitches=result[i].TextOptions?.Where(o=>o.PitchNum.HasValue).ToArray();
                if(key is null||pitches is not{Length:>0}||pitches.Any(p=>string.IsNullOrEmpty(p.PtsPitchId)))continue;
                var fingerprint=key+"|"+string.Join(";",pitches.Select(p=>p.PtsPitchId));
                if(!seen.Add(fingerprint)){game.Diagnostics.Add(new ParserDiagnostic{GameId=game.GameId,Severity=DiagnosticSeverity.Warning,Code="DUPLICATE_TRACKED_PA",Message=$"동일 투구 ID·상태·결과의 중복 타석 제거: relay {result[i].No}"});result.RemoveAt(i);}
            }
            return result;
        }

        private static List<TextRelayPlay> OrderChronologically(List<TextRelayPlay> sourcePlays)
        {
            if (sourcePlays.Count < 2)
            {
                return sourcePlays.ToList();
            }

            var firstNo = sourcePlays[0].No;
            var lastNo = sourcePlays[^1].No;
            if (firstNo.HasValue && lastNo.HasValue)
            {
                if (firstNo.Value < lastNo.Value)
                {
                    return sourcePlays.ToList();
                }

                if (firstNo.Value > lastNo.Value)
                {
                    return sourcePlays.AsEnumerable().Reverse().ToList();
                }
            }

            // Verified Naver relay payloads are newest-first. Preserve that as the fallback
            // when source numbering is missing or inconclusive.
            return sourcePlays.AsEnumerable().Reverse().ToList();
        }

        private static Dictionary<int, int> CountSourceSeqNos(List<TextRelayPlay> plays)
        {
            return plays
                .SelectMany(play => play.TextOptions ?? new List<TextOption>())
                .Where(option => option.Seqno.HasValue)
                .GroupBy(option => option.Seqno!.Value)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        private static void AddDuplicateSeqNoDiagnostics(
            NormalizedGame normalized,
            Dictionary<int, int> duplicateSeqNos)
        {
            foreach (var pair in duplicateSeqNos.OrderBy(pair => pair.Key))
            {
                normalized.Diagnostics.Add(new ParserDiagnostic
                {
                    Severity = DiagnosticSeverity.Info,
                    Code = "DUPLICATE_SOURCE_SEQNO",
                    Message = $"Source seqno {pair.Key} occurs {pair.Value} times. EventId remains unique by relay and option index.",
                    GameId = normalized.GameId,
                    SourceSeqNo = pair.Key,
                });
            }
        }

        private static NormalizedGame CreateGameMetadata(GameInfo? game, TextRelayData? relayData)
        {
            var gameId = game?.GameId ?? relayData?.GameId ?? string.Empty;
            return new NormalizedGame
            {
                GameId = gameId,
                SeasonYear = game?.SeasonYear,
                SuperCategoryId = game?.SuperCategoryId,
                UpperCategoryId = game?.UpperCategoryId,
                UpperCategoryName = game?.UpperCategoryName,
                CategoryId = game?.CategoryId,
                CategoryName = game?.CategoryName,
                RoundCode = game?.RoundCode,
                CompetitionType = ClassifyCompetition(game),
                GameDate = game?.GameDate,
                GameDateTime = game?.GameDateTime,
                Stadium = game?.Stadium,
                StatusCode = game?.StatusCode,
                Winner = game?.Winner,
                AwayTeam = CreateTeamMetadata(TeamSide.Away, game?.AwayTeamCode, game?.AwayTeamName,
                    game?.AwayTeamScore, game?.AwayTeamRheb),
                HomeTeam = CreateTeamMetadata(TeamSide.Home, game?.HomeTeamCode, game?.HomeTeamName,
                    game?.HomeTeamScore, game?.HomeTeamRheb),
            };
        }


        private static GameCompetitionType ClassifyCompetition(GameInfo? game)
        {
            var text = string.Join(" ", new[]
            {
                game?.UpperCategoryName, game?.CategoryName, game?.RoundCode,
                game?.CategoryId, game?.SuperCategoryId
            }.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

            if (text.Contains("퓨처스") || text.Contains("futures"))
                return GameCompetitionType.Futures;
            if (text.Contains("올스타") || text.Contains("allstar") || text.Contains("all-star"))
                return GameCompetitionType.AllStar;
            if (text.Contains("시범") || text.Contains("preseason") || text.Contains("exhibition") ||
                text.Contains("kbo_ex") || text.Contains("kbo_pre"))
                return GameCompetitionType.Preseason;
            if (text.Contains("포스트") || text.Contains("postseason") || text.Contains("와일드카드") ||
                text.Contains("준플레이오프") || text.Contains("플레이오프") || text.Contains("한국시리즈") ||
                text.Contains("kbo_ps") || text.Contains("kbo_wc") || text.Contains("kbo_ks"))
                return GameCompetitionType.Postseason;
            // KBO 정규시즌은 네이버 경기의 roundCode가 정확히 "kbo_r"인 경우만 인정한다.
            // categoryId="kbo" 또는 화면 이름에 "정규"가 포함된 것만으로는 정규시즌으로 분류하지 않는다.
            if (string.Equals(game?.RoundCode?.Trim(), "kbo_r", StringComparison.OrdinalIgnoreCase))
                return GameCompetitionType.RegularSeason;

            return GameCompetitionType.Other;
        }

        private static TeamMetadata CreateTeamMetadata(
            TeamSide side,
            string? teamCode,
            string? teamName,
            int? score,
            List<int>? rheb)
        {
            return new TeamMetadata
            {
                Side = side,
                TeamCode = teamCode,
                TeamName = teamName,
                FinalScore = score ?? ValueAt(rheb, 0),
                FinalHits = ValueAt(rheb, 1),
                FinalErrors = ValueAt(rheb, 2),
                FinalWalks = ValueAt(rheb, 3),
            };
        }

        private static int? ValueAt(List<int>? values, int index)
        {
            return values != null && index >= 0 && index < values.Count ? values[index] : null;
        }

        private static void ExtractFinalLines(
            NormalizedGame normalized,
            TextRelayData relayData,
            ParsingContext context)
        {
            AddBattingLines(normalized, relayData.AwayLineup?.Batter, TeamSide.Away, context);
            AddBattingLines(normalized, relayData.HomeLineup?.Batter, TeamSide.Home, context);
            AddPitchingLines(normalized, relayData.AwayLineup?.Pitcher, TeamSide.Away, context);
            AddPitchingLines(normalized, relayData.HomeLineup?.Pitcher, TeamSide.Home, context);
        }

        private static void AddBattingLines(
            NormalizedGame normalized,
            List<LineupBatter>? source,
            TeamSide side,
            ParsingContext context)
        {
            if (source == null) return;
            foreach (var player in source)
            {
                normalized.BattingLines.Add(new GamePlayerBattingLine
                {
                    GameId = normalized.GameId,
                    TeamSide = side,
                    TeamCode = context.GetTeamCode(side),
                    Pcode = player.Pcode,
                    Name = player.Name,
                    BatOrder = player.BatOrder,
                    LineupSequence = player.Seqno,
                    Position = player.PosName,
                    BirthDateRaw = player.Birth,
                    Height = player.Height,
                    Weight = player.Weight,
                    BackNumber = player.Backnum,
                    HitType = player.HitType,
                    EnteredAsSubstitute = ParserUtilities.IsTrueString(player.Cin),
                    LeftGame = ParserUtilities.IsTrueString(player.Cout),
                    PlateAppearances = player.Pa,
                    AtBats = player.Ab,
                    Hits = player.Hit,
                    HomeRuns = player.Hr,
                    Walks = player.Bb,
                    HitByPitch = player.Hbp,
                    Strikeouts = player.So,
                    Runs = player.Run,
                    RunsBattedIn = player.Rbi,
                });
            }
        }

        private static void AddPitchingLines(
            NormalizedGame normalized,
            List<LineupPitcher>? source,
            TeamSide side,
            ParsingContext context)
        {
            if (source == null) return;
            foreach (var player in source)
            {
                normalized.PitchingLines.Add(new GamePlayerPitchingLine
                {
                    GameId = normalized.GameId,
                    TeamSide = side,
                    TeamCode = context.GetTeamCode(side),
                    Pcode = player.Pcode,
                    Name = player.Name,
                    BirthDateRaw = player.Birth,
                    Height = player.Height,
                    Weight = player.Weight,
                    BackNumber = player.Backnum,
                    HitType = player.HitType,
                    AppearanceSequence = player.Seqno,
                    InningsDisplay = player.Inn,
                    PitchCount = player.BallCount,
                    HitsAllowed = player.Hit,
                    HomeRunsAllowed = player.Hr,
                    Walks = player.Bb,
                    HitBatters = player.Hbp,
                    Strikeouts = player.Kk,
                    RunsAllowed = player.Run,
                    EarnedRuns = player.Er,
                    WildPitches = player.Wp,
                });
            }
        }

        private static void FinalizeSummary(
            NormalizedGame normalized,
            IReadOnlyCollection<TextRelayPlay> plays,
            Dictionary<int, int> seqNoCounts)
        {
            var rawEvents = plays.SelectMany(play => play.TextOptions ?? new List<TextOption>()).ToList();
            normalized.Summary = new ParserSummary
            {
                RawRelayGroupCount = plays.Count,
                RawEventCount = rawEvents.Count,
                RawPitchEventCount = rawEvents.Count(option => option.Type == 1),
                RawPtsCount = plays.Sum(play => play.PtsOptions?.Count ?? 0),
                DuplicateSourceSeqNoOccurrenceCount = seqNoCounts.Where(pair => pair.Value > 1)
                    .Sum(pair => pair.Value - 1),
                CompletedPlateAppearanceCount = normalized.PlateAppearances.Count(pa => pa.Status == PlateAppearanceStatus.Completed),
                InterruptedPlateAppearanceCount = normalized.PlateAppearances.Count(pa => pa.Status == PlateAppearanceStatus.InterruptedByRunnerOut),
                PrePlateSubstitutionGroupCount = normalized.RelayGroups.Count(g => g.GroupType == RelayGroupType.PrePlateSubstitution),
                InningMarkerGroupCount = normalized.RelayGroups.Count(g => g.GroupType == RelayGroupType.InningMarker),
                GameSummaryGroupCount = normalized.RelayGroups.Count(g => g.GroupType == RelayGroupType.GameSummary),
                PitchEventCount = normalized.PitchEvents.Count,
                PtsMatchedPitchCount = normalized.PitchEvents.Count(p => p.HasPtsTracking),
                PtsMissingPitchCount = normalized.PitchEvents.Count(p => !p.HasPtsTracking),
                RunnerEventCount = normalized.RunnerEvents.Count,
                PlayerChangeEventCount = normalized.PlayerChanges.Count,
                AdministrativeEventCount = normalized.AdministrativeEvents.Count,
                UnknownRelayGroupCount = normalized.RelayGroups.Count(g => g.GroupType == RelayGroupType.Unknown),
                UnknownRawEventTypeCount = normalized.Events.Count(e => e.EventType == NormalizedEventType.Unknown),
                UnknownBattingResultCount = normalized.PlateAppearances.Count(pa => pa.Status == PlateAppearanceStatus.Completed && !pa.Outcome.WasRecognized),
                UnparsedRunnerEventCount = normalized.RunnerEvents.Count(e => !e.WasParsed),
                UnparsedPlayerChangeCount = normalized.PlayerChanges.Count(e => !e.WasParsed),
                UnknownAdministrativeEventCount = normalized.AdministrativeEvents.Count(e => !e.WasRecognized),
                PtsCalculationFailureCount = normalized.Diagnostics.Count(d => d.Code == "PTS_CALCULATION_FAILED"),
                FinalLineBattingMismatchCount = normalized.Diagnostics.Count(d => d.Code.StartsWith("FINAL_LINE_", StringComparison.Ordinal)),
                WarningCount = normalized.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning),
                ErrorCount = normalized.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            };
        }

        private static void AddDiagnostic(
            NormalizedGame normalized,
            DiagnosticSeverity severity,
            string code,
            string message,
            RelayGroup group,
            NormalizedEvent? normalizedEvent)
        {
            normalized.Diagnostics.Add(new ParserDiagnostic
            {
                Severity = severity,
                Code = code,
                Message = message,
                GameId = normalized.GameId,
                RelayGroupId = group.RelayGroupId,
                EventId = normalizedEvent?.EventId,
                SourceRelayNo = group.SourceRelayNo,
                SourceSeqNo = normalizedEvent?.SourceSeqNo,
            });
        }

        private static NormalizedEvent? FindEvent(
            IEnumerable<(TextOption Raw, NormalizedEvent Normalized)> pairs,
            string? eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return null;
            }

            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Normalized.EventId, eventId, StringComparison.Ordinal))
                {
                    return pair.Normalized;
                }
            }

            return null;
        }

        private static bool IsCompletelyEmpty(BatterRecord record)
        {
            return record.Pcode == null
                && record.Name == null
                && record.BatOrder == null
                && record.Pa == null
                && record.Ab == null
                && record.Hit == null
                && record.Bb == null
                && record.Hbp == null;
        }
    }
}
