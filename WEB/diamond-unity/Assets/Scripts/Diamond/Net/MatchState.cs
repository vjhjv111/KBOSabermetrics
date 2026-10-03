using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Diamond.Model;

namespace Diamond.Net
{
    /// <summary>
    /// Read-only view of a /api/diamond/match response (full friendly match): the live game (inning, outs, score, runners),
    /// the current plate-appearance action, the two clubs' rosters and the match info. Parsed leniently from JSON.
    /// </summary>
    public sealed class MatchState
    {
        public readonly JObject Raw;
        public readonly string Code;
        public readonly long Version;
        public readonly string MyTeam, HostTeam, GuestTeam;
        public readonly ActionView Action;
        public readonly long ServerNow;
        readonly JToken _game;
        readonly Dictionary<string, JToken> _players = new Dictionary<string, JToken>();
        readonly Dictionary<string, string> _teamNames = new Dictionary<string, string>();

        public MatchState(JObject raw)
        {
            Raw = raw;
            var match = raw["match"];
            Code = match?.Value<string>("code") ?? "";
            Version = match?.Value<long>("version") ?? 0;
            MyTeam = match?.Value<string>("team") ?? "";
            HostTeam = match?.Value<string>("hostTeam") ?? "";
            GuestTeam = match?.Value<string>("guestTeam") ?? "";
            ServerNow = raw.Value<long?>("serverNow") ?? 0;
            _game = raw["save"]?["game"];
            var action = raw["action"];
            Action = action != null && action.Type == JTokenType.Object ? DiamondJson.Parse<ActionView>(action.ToString()) : null;
            foreach (var team in raw["save"]?["teams"] ?? Enumerable.Empty<JToken>())
            {
                _teamNames[team.Value<string>("code") ?? ""] = team.Value<string>("name") ?? team.Value<string>("code") ?? "";
                foreach (var key in new[] { "batters", "pitchers" })
                    foreach (var p in team[key] ?? Enumerable.Empty<JToken>())
                        if (p.Value<string>("id") is string id) _players[id] = p;
            }
        }

        public bool HasGame => _game != null && _game.Type == JTokenType.Object;
        public bool Complete => HasGame && _game.Value<bool>("complete");
        public string HomeTeam => _game?.Value<string>("homeTeam") ?? "";
        public string AwayTeam => _game?.Value<string>("awayTeam") ?? "";
        public int Inning => _game?.Value<int?>("inning") ?? 1;
        public bool TopHalf => (_game?.Value<string>("half") ?? "top") == "top";
        /// <summary>Id of the batter due up (from the lineup and batting order). The action view keeps the previous batter until the next pitch is created.</summary>
        public string UpcomingBatter
        {
            get
            {
                var side = TopHalf ? "away" : "home";
                var lineup = _game?[side + "Lineup"] as JArray;
                var order = _game?.Value<int?>(side + "Order") ?? 0;
                return lineup != null && lineup.Count > 0 ? lineup[order % lineup.Count].Value<string>() : null;
            }
        }

        /// <summary>Id of the pitcher on the mound for the defending club.</summary>
        public string UpcomingPitcher => _game?.Value<string>(TopHalf ? "homePitcher" : "awayPitcher");

        public int Outs => _game?.Value<int?>("outs") ?? 0;
        public int HomeRuns => _game?.Value<int?>("homeRuns") ?? 0;
        public int AwayRuns => _game?.Value<int?>("awayRuns") ?? 0;
        public int HomeHits => _game?.Value<int?>("homeHits") ?? 0;
        public int AwayHits => _game?.Value<int?>("awayHits") ?? 0;
        public int[] HomeLine => Line("homeLine");
        public int[] AwayLine => Line("awayLine");
        public string EndReason => _game?.Value<string>("endReason") ?? "";

        /// <summary>The team currently at bat.</summary>
        public string BattingTeam => TopHalf ? AwayTeam : HomeTeam;
        public bool IBat => BattingTeam == MyTeam;

        /// <summary>Player ids on first, second and third base (null when empty).</summary>
        public string[] BaseRunners
        {
            get
            {
                var result = new string[3];
                var bases = _game?["bases"] as JArray;
                for (var i = 0; bases != null && i < 3 && i < bases.Count; i++)
                    if (bases[i] != null && bases[i].Type == JTokenType.Object) result[i] = bases[i].Value<string>("playerId");
                return result;
            }
        }

        public string[] RecentEvents(int count)
        {
            var events = _game?["events"] as JArray;
            if (events == null) return Array.Empty<string>();
            return events.Skip(Math.Max(0, events.Count - count)).Select(e => e.ToString()).ToArray();
        }

        int[] Line(string key) => (_game?[key] as JArray)?.Select(x => x.Value<int?>() ?? 0).ToArray() ?? Array.Empty<int>();

        public string PlayerName(string id) => id != null && _players.TryGetValue(id, out var p) ? p.Value<string>("name") ?? id : id ?? "";
        public JToken Player(string id) => id != null && _players.TryGetValue(id, out var p) ? p : null;
        public string TeamName(string code) => _teamNames.TryGetValue(code ?? "", out var n) ? n : code ?? "";

        /// <summary>"R", "L" or "S" (switch) for a batter; null if unknown.</summary>
        public string Bats(string id) => Player(id)?["profile"]?.Value<string>("bats");
        /// <summary>"R" or "L" for a pitcher; null if unknown.</summary>
        public string Throws(string id) => Player(id)?["profile"]?.Value<string>("throws");
        /// <summary>"overhand", "sidearm" or "underhand"; null if unknown.</summary>
        public string Delivery(string id) => Player(id)?["profile"]?.Value<string>("delivery");
    }
}
