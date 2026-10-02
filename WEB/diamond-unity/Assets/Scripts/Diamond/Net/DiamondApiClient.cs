using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Diamond.Model;

namespace Diamond.Net
{
    /// <summary>
    /// Thin client for the site's /api/diamond/action endpoints. The server stays authoritative for every
    /// result; Unity only replays them. Requires the site's session cookie and CSRF token
    /// (GET /api/session -> csrfToken, sent as X-CSRF-TOKEN on POST).
    /// </summary>
    public sealed class DiamondApiClient
    {
        readonly string _baseUrl;
        string _csrf = "";

        public DiamondApiClient(string baseUrl) { _baseUrl = baseUrl.TrimEnd('/'); }

        [Serializable] class SessionDto { public string csrfToken; }

        async Task<string> Send(UnityWebRequest request)
        {
            request.timeout = 12;
            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            var body = request.downloadHandler?.text ?? "";
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"HTTP {(int)request.responseCode}: {(string.IsNullOrEmpty(body) ? request.error : body)}");
            return body;
        }

        public async Task EnsureSession()
        {
            if (_csrf.Length > 0) return;
            using var req = UnityWebRequest.Get(_baseUrl + "/api/session");
            req.SetRequestHeader("Cache-Control", "no-store");
            _csrf = JsonUtility.FromJson<SessionDto>(await Send(req)).csrfToken ?? "";
        }

        /// <summary>GET /api/diamond/roster: season, teams, batters (with profile.bats) and pitchers (with profile.throws/delivery).</summary>
        public async Task<JObject> GetRoster()
        {
            using var req = UnityWebRequest.Get(_baseUrl + "/api/diamond/roster");
            return JObject.Parse(await Send(req));
        }

        /// <summary>POST /api/diamond/match (full friendly match) with the session CSRF token; returns the raw JSON response.</summary>
        public async Task<JObject> PostMatchRaw(JObject body)
        {
            await EnsureSession();
            using var req = new UnityWebRequest(_baseUrl + "/api/diamond/match", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-CSRF-TOKEN", _csrf);
            try { return JObject.Parse(await Send(req)); }
            catch (Exception e) when (e.Message.Contains("CSRF")) { _csrf = ""; throw; }
        }

        public async Task<JObject> GetMatchRaw(string code)
        {
            using var req = UnityWebRequest.Get(_baseUrl + "/api/diamond/match?code=" + UnityWebRequest.EscapeURL(code));
            return JObject.Parse(await Send(req));
        }

        public async Task<ActionView> Get(string code)
        {
            using var req = UnityWebRequest.Get(_baseUrl + "/api/diamond/action?code=" + UnityWebRequest.EscapeURL(code));
            return DiamondJson.Parse<ActionView>(await Send(req));
        }

        public async Task<ActionView> Post(object body)
        {
            await EnsureSession();
            var json = JsonConvert.SerializeObject(body);
            using var req = new UnityWebRequest(_baseUrl + "/api/diamond/action", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-CSRF-TOKEN", _csrf);
            try { return DiamondJson.Parse<ActionView>(await Send(req)); }
            catch (Exception e) when (e.Message.Contains("CSRF")) { _csrf = ""; throw; }
        }
    }
}
