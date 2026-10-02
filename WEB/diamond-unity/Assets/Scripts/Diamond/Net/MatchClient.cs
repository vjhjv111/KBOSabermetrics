using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Diamond.Net
{
    /// <summary>
    /// Client for the full friendly match (/api/diamond/match). Every command carries a fresh requestId and the last seen match
    /// version; the server rejects stale versions, so state is only advanced from responses.
    /// </summary>
    public sealed class MatchClient
    {
        readonly DiamondApiClient _api;
        public MatchState State { get; private set; }

        public MatchClient(DiamondApiClient api) { _api = api; }

        static string RequestId() => "u" + Guid.NewGuid().ToString("N").Substring(0, 20);

        public async Task<MatchState> Create(int season, string hostTeam, string guestTeam, string pace)
        {
            var body = new JObject
            {
                ["op"] = "create", ["requestId"] = RequestId(), ["season"] = season,
                ["hostTeam"] = hostTeam, ["guestTeam"] = guestTeam, ["mode"] = "ai", ["pace"] = pace,
            };
            State = new MatchState(await _api.PostMatchRaw(body));
            return State;
        }

        /// <summary>Sends a command against the current match version (ready, swing, take, tick, sim-half, sim-game...).</summary>
        public async Task<MatchState> Command(string op, object extra = null)
        {
            var body = new JObject
            {
                ["op"] = op, ["requestId"] = RequestId(), ["code"] = State.Code, ["version"] = State.Version,
            };
            if (extra != null) body.Merge(JObject.FromObject(extra));
            State = new MatchState(await _api.PostMatchRaw(body));
            return State;
        }

        public async Task<MatchState> Refresh()
        {
            State = new MatchState(await _api.GetMatchRaw(State.Code));
            return State;
        }
    }
}
