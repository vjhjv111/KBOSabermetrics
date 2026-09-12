using System;
using System.Collections.Generic;
using System.Linq;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    internal sealed class ParsingContext
    {
        private readonly Dictionary<string, PlayerIdentity> _playersByCode = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PlayerIdentity>> _playersByName = new(StringComparer.Ordinal);
        private readonly Dictionary<TeamSide, Dictionary<int, PlayerIdentity>> _activeBattingSlots = new()
        {
            [TeamSide.Away] = new Dictionary<int, PlayerIdentity>(),
            [TeamSide.Home] = new Dictionary<int, PlayerIdentity>(),
        };

        public ParsingContext(GameInfo? game, TextRelayData relayData)
        {
            Game = game;
            RelayData = relayData;
            GameId = game?.GameId ?? relayData.GameId ?? string.Empty;

            RegisterLineup(TeamSide.Away, relayData.AwayLineup);
            RegisterLineup(TeamSide.Home, relayData.HomeLineup);
            RegisterEntry(TeamSide.Away, relayData.AwayEntry);
            RegisterEntry(TeamSide.Home, relayData.HomeEntry);
            InitializeActiveSlots(TeamSide.Away, relayData.AwayLineup?.Batter);
            InitializeActiveSlots(TeamSide.Home, relayData.HomeLineup?.Batter);
        }

        public GameInfo? Game { get; }
        public TextRelayData RelayData { get; }
        public string GameId { get; }

        public string? GetTeamCode(TeamSide side)
        {
            return side switch
            {
                TeamSide.Away => Game?.AwayTeamCode,
                TeamSide.Home => Game?.HomeTeamCode,
                _ => null,
            };
        }

        public string? GetTeamName(TeamSide side)
        {
            return side switch
            {
                TeamSide.Away => Game?.AwayTeamName,
                TeamSide.Home => Game?.HomeTeamName,
                _ => null,
            };
        }

        public PlayerIdentity? FindByPcode(string? pcode)
        {
            if (string.IsNullOrWhiteSpace(pcode))
            {
                return null;
            }

            return _playersByCode.TryGetValue(pcode, out var player) ? player : null;
        }

        public PlayerIdentity? FindByName(string? name, TeamSide preferredSide = TeamSide.Unknown)
        {
            if (string.IsNullOrWhiteSpace(name) || !_playersByName.TryGetValue(name.Trim(), out var candidates))
            {
                return null;
            }

            if (preferredSide != TeamSide.Unknown)
            {
                var preferred = candidates.Where(p => p.TeamSide == preferredSide).ToArray();
                return preferred.Length == 1 ? preferred[0] : null;
            }

            return candidates.Count == 1 ? candidates[0] : null;
        }

        public TeamSide ResolveTeam(string? pcode, string? name, TeamSide fallback = TeamSide.Unknown)
        {
            var player = FindByPcode(pcode) ?? FindByName(name, fallback);
            return player?.TeamSide ?? fallback;
        }

        public PlayerIdentity? GetActivePlayer(TeamSide teamSide, int? batOrder)
        {
            if (!batOrder.HasValue || !_activeBattingSlots.TryGetValue(teamSide, out var slots))
            {
                return null;
            }

            return slots.TryGetValue(batOrder.Value, out var player) ? player : null;
        }

        public int? FindActiveBatOrder(TeamSide teamSide, string? pcode, string? name)
        {
            if (!_activeBattingSlots.TryGetValue(teamSide, out var slots))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(pcode))
            {
                var byCode = slots.Where(pair => string.Equals(pair.Value.Pcode, pcode, StringComparison.Ordinal)).ToArray();
                return byCode.Length == 1 ? byCode[0].Key : null;
            }
            var byName = slots.Where(pair => !string.IsNullOrWhiteSpace(name) && string.Equals(pair.Value.Name, name, StringComparison.Ordinal)).ToArray();
            return byName.Length == 1 ? byName[0].Key : null;
        }

        public GameStateSnapshot NormalizeState(CurrentGameState? raw, TeamSide battingSide)
        {
            if (raw == null)
            {
                return new GameStateSnapshot();
            }

            var firstSlot = NormalizeBaseSlot(ParserUtilities.ParseInt(raw.Base1));
            var secondSlot = NormalizeBaseSlot(ParserUtilities.ParseInt(raw.Base2));
            var thirdSlot = NormalizeBaseSlot(ParserUtilities.ParseInt(raw.Base3));
            var firstRunner = GetActivePlayer(battingSide, firstSlot);
            var secondRunner = GetActivePlayer(battingSide, secondSlot);
            var thirdRunner = GetActivePlayer(battingSide, thirdSlot);
            var pitcher = FindByPcode(raw.Pitcher);
            var batter = FindByPcode(raw.Batter);

            return new GameStateSnapshot
            {
                HomeScore = ParserUtilities.ParseInt(raw.HomeScore),
                AwayScore = ParserUtilities.ParseInt(raw.AwayScore),
                HomeHits = ParserUtilities.ParseInt(raw.HomeHit),
                AwayHits = ParserUtilities.ParseInt(raw.AwayHit),
                HomeWalks = ParserUtilities.ParseInt(raw.HomeBallFour),
                AwayWalks = ParserUtilities.ParseInt(raw.AwayBallFour),
                HomeErrors = ParserUtilities.ParseInt(raw.HomeError),
                AwayErrors = ParserUtilities.ParseInt(raw.AwayError),
                PitcherPcode = raw.Pitcher,
                PitcherName = pitcher?.Name,
                BatterPcode = raw.Batter,
                BatterName = batter?.Name,
                Balls = ParserUtilities.ParseInt(raw.Ball),
                Strikes = ParserUtilities.ParseInt(raw.Strike),
                Outs = ParserUtilities.ParseInt(raw.Out),
                FirstBaseSlot = firstSlot,
                SecondBaseSlot = secondSlot,
                ThirdBaseSlot = thirdSlot,
                FirstBaseRunnerPcode = firstRunner?.Pcode,
                FirstBaseRunnerName = firstRunner?.Name,
                SecondBaseRunnerPcode = secondRunner?.Pcode,
                SecondBaseRunnerName = secondRunner?.Name,
                ThirdBaseRunnerPcode = thirdRunner?.Pcode,
                ThirdBaseRunnerName = thirdRunner?.Name,
            };
        }

        public PlayerIdentity RegisterOrUpdatePlayer(
            TeamSide teamSide,
            string? pcode,
            string? name,
            int? batOrder = null,
            int? lineupSequence = null,
            string? position = null,
            string? hitType = null,
            string? pitchingStyle = null)
        {
            PlayerIdentity? player = null;
            if (!string.IsNullOrWhiteSpace(pcode))
            {
                _playersByCode.TryGetValue(pcode, out player);
            }

            if (player == null)
            {
                var byName = FindByName(name, teamSide);
                // A new known ID is a different player, even if an opponent or teammate has the
                // same name. Reusing that object's identity corrupts both code indexes and slots.
                if (byName != null && (string.IsNullOrWhiteSpace(pcode) || string.IsNullOrWhiteSpace(byName.Pcode) || byName.Pcode == pcode))
                    player = byName;
            }
            if (player == null)
            {
                player = new PlayerIdentity
                {
                    Pcode = pcode,
                    Name = name,
                    TeamSide = teamSide,
                    TeamCode = GetTeamCode(teamSide),
                };
            }

            if (!string.IsNullOrWhiteSpace(pcode)) player.Pcode = pcode;
            if (!string.IsNullOrWhiteSpace(name)) player.Name = name;
            if (teamSide != TeamSide.Unknown) player.TeamSide = teamSide;
            player.TeamCode ??= GetTeamCode(player.TeamSide);
            if (batOrder.HasValue) player.BatOrder = batOrder;
            if (lineupSequence.HasValue) player.LineupSequence = lineupSequence;
            if (!string.IsNullOrWhiteSpace(position)) player.Position = position;
            if (!string.IsNullOrWhiteSpace(hitType)) player.HitType = hitType;
            if (!string.IsNullOrWhiteSpace(pitchingStyle)) player.PitchingStyle = pitchingStyle;

            IndexPlayer(player);
            return player;
        }

        public void ApplyPlayerChange(PlayerChangeEvent change)
        {
            var teamSide = change.ChangedTeamSide;
            if (teamSide == TeamSide.Unknown)
            {
                teamSide = ResolveTeam(change.InPlayerPcode, change.InPlayerName,
                    ResolveTeam(change.OutPlayerPcode, change.OutPlayerName, change.BattingSide));
                change.ChangedTeamSide = teamSide;
                change.TeamCode = GetTeamCode(teamSide);
            }

            if (change.ChangeType == PlayerChangeType.Substitution || change.ChangeType == PlayerChangeType.TextOnly)
            {
                var order = change.BatOrder
                    ?? FindActiveBatOrder(teamSide, change.OutPlayerPcode, change.OutPlayerName);
                change.BatOrder = order;

                var incoming = RegisterOrUpdatePlayer(
                    teamSide,
                    change.InPlayerPcode,
                    change.InPlayerName,
                    order,
                    position: change.InPosition);

                if (order.HasValue && order.Value >= 1 && order.Value <= 9
                    && _activeBattingSlots.TryGetValue(teamSide, out var slots))
                {
                    slots[order.Value] = incoming;
                }
            }
            else if (change.ChangeType == PlayerChangeType.PositionShift)
            {
                var player = RegisterOrUpdatePlayer(
                    teamSide,
                    change.ShiftPlayerPcode,
                    change.ShiftPlayerName,
                    change.BatOrder,
                    position: change.NewPosition);

                if (change.BatOrder.HasValue
                    && change.BatOrder.Value >= 1
                    && change.BatOrder.Value <= 9
                    && _activeBattingSlots.TryGetValue(teamSide, out var slots))
                {
                    slots[change.BatOrder.Value] = player;
                }
            }
        }

        private void RegisterLineup(TeamSide side, LineupTeam? lineup)
        {
            if (lineup?.Batter != null)
            {
                foreach (var batter in lineup.Batter)
                {
                    RegisterOrUpdatePlayer(side, batter.Pcode, batter.Name, batter.BatOrder, batter.Seqno,
                        batter.PosName, batter.HitType);
                }
            }

            if (lineup?.Pitcher != null)
            {
                foreach (var pitcher in lineup.Pitcher)
                {
                    RegisterOrUpdatePlayer(side, pitcher.Pcode, pitcher.Name, lineupSequence: pitcher.Seqno,
                        position: "투수", hitType: pitcher.HitType);
                }
            }
        }

        private void RegisterEntry(TeamSide side, EntryTeam? entry)
        {
            if (entry?.Batter != null)
            {
                foreach (var player in entry.Batter)
                {
                    RegisterOrUpdatePlayer(side, player.Pcode, player.Name, position: player.Pos,
                        hitType: player.Hittype, pitchingStyle: player.PitchingStyle);
                }
            }

            if (entry?.Pitcher != null)
            {
                foreach (var player in entry.Pitcher)
                {
                    RegisterOrUpdatePlayer(side, player.Pcode, player.Name, position: player.Pos,
                        hitType: player.Hittype, pitchingStyle: player.PitchingStyle);
                }
            }
        }

        private void InitializeActiveSlots(TeamSide side, List<LineupBatter>? batters)
        {
            if (batters == null || !_activeBattingSlots.TryGetValue(side, out var slots))
            {
                return;
            }

            foreach (var group in batters
                         .Where(b => b.BatOrder.HasValue && b.BatOrder.Value >= 1 && b.BatOrder.Value <= 9)
                         .GroupBy(b => b.BatOrder!.Value))
            {
                var starter = group.OrderBy(b => b.Seqno ?? int.MaxValue).First();
                var player = RegisterOrUpdatePlayer(side, starter.Pcode, starter.Name, starter.BatOrder,
                    starter.Seqno, starter.PosName, starter.HitType);
                slots[group.Key] = player;
            }
        }

        private void IndexPlayer(PlayerIdentity player)
        {
            if (!string.IsNullOrWhiteSpace(player.Pcode))
            {
                _playersByCode[player.Pcode] = player;
            }

            if (!string.IsNullOrWhiteSpace(player.Name))
            {
                if (!_playersByName.TryGetValue(player.Name, out var candidates))
                {
                    candidates = new List<PlayerIdentity>();
                    _playersByName[player.Name] = candidates;
                }

                if (!candidates.Contains(player))
                {
                    candidates.Add(player);
                }
            }
        }

        private static int? NormalizeBaseSlot(int? value)
        {
            return value.HasValue && value.Value > 0 ? value : null;
        }
    }
}
