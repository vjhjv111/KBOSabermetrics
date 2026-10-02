using UnityEngine;

namespace Diamond.Stadium
{
    /// <summary>
    /// Broadcast-style heads-up display drawn with IMGUI: line score (innings, R, H), inning, ball/strike/out count, base
    /// runners, batter/pitcher lines, a result banner and a short play log. Fed by the match driver; it holds no game logic.
    /// </summary>
    public sealed class GameHud : MonoBehaviour
    {
        // Line score
        string _awayName = "", _homeName = "";
        int[] _awayLine = new int[0], _homeLine = new int[0];
        int _awayRuns, _homeRuns, _awayHits, _homeHits;
        int _inning = 1; bool _top = true;
        // Situation
        int _balls, _strikes, _outs;
        readonly bool[] _bases = new bool[3];
        string _batterLine = "", _pitcherLine = "", _status = "";
        string[] _log = new string[0];
        // Banner
        string _banner = ""; Color _bannerColor = Color.white; float _bannerUntil;
        // Pitching controls
        string[] _pitchItems = new string[0]; int _pitchSelected;
        bool _gaugeVisible; float _gaugePos;
        // Resources
        Texture2D _white, _circle;
        GUIStyle _label, _small, _big, _banner1;

        const int Columns = 9;

        void Awake()
        {
            _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply();
            const int n = 32;
            _circle = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                _circle.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(n / 2f - d)));
            }
            _circle.Apply();
        }

        public void SetLineScore(string awayName, string homeName, int[] awayLine, int[] homeLine, int awayRuns, int homeRuns, int awayHits, int homeHits)
        {
            _awayName = awayName; _homeName = homeName; _awayLine = awayLine; _homeLine = homeLine;
            _awayRuns = awayRuns; _homeRuns = homeRuns; _awayHits = awayHits; _homeHits = homeHits;
        }

        public void SetSituation(int inning, bool top, int balls, int strikes, int outs, string first, string second, string third)
        {
            _inning = inning; _top = top; _balls = balls; _strikes = strikes; _outs = outs;
            _bases[0] = first != null; _bases[1] = second != null; _bases[2] = third != null;
        }

        public void SetPlayers(string batterLine, string pitcherLine) { _batterLine = batterLine ?? ""; _pitcherLine = pitcherLine ?? ""; }
        public void SetStatus(string status) { _status = status ?? ""; }
        public void SetPitchMenu(string[] items, int selected) { _pitchItems = items ?? new string[0]; _pitchSelected = selected; }

        /// <summary>Release gauge: <paramref name="pos"/> runs 0..1; the sweet spot is the centre.</summary>
        public void SetGauge(bool visible, float pos) { _gaugeVisible = visible; _gaugePos = pos; }

        public void SetLog(string[] lines) { _log = lines ?? new string[0]; }

        public void ShowBanner(string text, Color color, float seconds)
        {
            _banner = text; _bannerColor = color; _bannerUntil = Time.realtimeSinceStartup + seconds;
        }

        void Styles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _small = new GUIStyle(_label) { fontSize = 17, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
            _big = new GUIStyle(_label) { fontSize = 30 };
            _banner1 = new GUIStyle(_label) { fontSize = 64 };
        }

        void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old; }

        void Text(Rect r, string s, GUIStyle style, Color? color = null)
        {
            var old = style.normal.textColor;
            if (color != null) style.normal.textColor = color.Value;
            GUI.Label(r, s, style);
            style.normal.textColor = old;
        }

        void Dot(Vector2 centre, float size, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), _circle); GUI.color = old; }

        void OnGUI()
        {
            if (_white == null) return;
            Styles();
            var scale = Screen.height / 1080f;
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var width = Screen.width / scale;
            var height = Screen.height / scale;

            DrawLineScore();
            DrawSituation(24, height - 190);
            DrawPlayers(width, height);
            DrawLog(width, height);
            DrawBanner(width);
            DrawPitchControls(width, height);
            if (_status.Length > 0) Text(new Rect(24, height - 36, 900, 28), _status, _small, new Color(1, 1, 1, 0.7f));
            GUI.matrix = previous;
        }

        void DrawLineScore()
        {
            const float x = 24, y = 20, nameW = 150, cell = 36, rowH = 34;
            var innings = Mathf.Max(Columns, Mathf.Max(_awayLine.Length, _homeLine.Length));
            var w = nameW + innings * cell + cell * 2 + 8;
            Fill(new Rect(x, y, w, rowH * 3 + 8), new Color(0.05f, 0.1f, 0.16f, 0.88f));
            // Header
            for (var i = 0; i < innings; i++)
                Text(new Rect(x + nameW + i * cell, y + 4, cell, rowH), (i + 1).ToString(), _small, i + 1 == _inning ? new Color(1f, 0.85f, 0.3f) : new Color(1, 1, 1, 0.55f));
            Text(new Rect(x + nameW + innings * cell, y + 4, cell, rowH), "R", _small, new Color(1, 1, 1, 0.8f));
            Text(new Rect(x + nameW + (innings + 1) * cell, y + 4, cell, rowH), "H", _small, new Color(1, 1, 1, 0.8f));
            DrawTeamRow(x, y + 4 + rowH, nameW, cell, rowH, innings, _awayName, _awayLine, _awayRuns, _awayHits, _top);
            DrawTeamRow(x, y + 4 + rowH * 2, nameW, cell, rowH, innings, _homeName, _homeLine, _homeRuns, _homeHits, !_top);
        }

        void DrawTeamRow(float x, float y, float nameW, float cell, float rowH, int innings, string name, int[] line, int runs, int hits, bool batting)
        {
            if (batting) Fill(new Rect(x + 4, y + 8, 6, rowH - 16), new Color(1f, 0.85f, 0.3f));
            Text(new Rect(x + 14, y, nameW - 14, rowH), name, _small, Color.white);
            for (var i = 0; i < innings; i++)
                Text(new Rect(x + nameW + i * cell, y, cell, rowH), i < line.Length ? line[i].ToString() : "", _small, Color.white);
            Text(new Rect(x + nameW + innings * cell, y, cell, rowH), runs.ToString(), _label, new Color(1f, 0.85f, 0.3f));
            Text(new Rect(x + nameW + (innings + 1) * cell, y, cell, rowH), hits.ToString(), _small, Color.white);
        }

        void DrawSituation(float x, float y)
        {
            Fill(new Rect(x, y, 330, 150), new Color(0.05f, 0.1f, 0.16f, 0.88f));
            Text(new Rect(x + 12, y + 8, 150, 36), $"{_inning}회 {(_top ? "초" : "말")}", _big, new Color(1f, 0.85f, 0.3f));
            // Base diamond
            var c = new Vector2(x + 255, y + 80);
            DrawBase(c + new Vector2(0, -34), _bases[1]);   // second
            DrawBase(c + new Vector2(34, 0), _bases[0]);    // first
            DrawBase(c + new Vector2(-34, 0), _bases[2]);   // third
            DrawBase(c + new Vector2(0, 34), false, true);  // home
            // Count
            DrawCount(x + 16, y + 56, "B", _balls, 4, new Color(0.3f, 0.8f, 0.4f));
            DrawCount(x + 16, y + 86, "S", _strikes, 3, new Color(1f, 0.8f, 0.2f));
            DrawCount(x + 16, y + 116, "O", _outs, 3, new Color(1f, 0.35f, 0.3f));
        }

        void DrawBase(Vector2 centre, bool occupied, bool home = false)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(45, centre);
            Fill(new Rect(centre.x - 13, centre.y - 13, 26, 26), occupied ? new Color(1f, 0.85f, 0.2f) : home ? new Color(1, 1, 1, 0.35f) : new Color(1, 1, 1, 0.22f));
            GUI.matrix = old;
        }

        void DrawCount(float x, float y, string name, int value, int max, Color on)
        {
            Text(new Rect(x, y - 12, 26, 28), name, _small, new Color(1, 1, 1, 0.8f));
            for (var i = 0; i < max; i++) Dot(new Vector2(x + 52 + i * 26, y + 2), 18, i < value ? on : new Color(1, 1, 1, 0.2f));
        }

        void DrawPlayers(float width, float height)
        {
            if (_batterLine.Length == 0 && _pitcherLine.Length == 0) return;
            const float w = 640, h = 78;
            var x = (width - w) / 2; var y = height - h - 22;
            Fill(new Rect(x, y, w, h), new Color(0.05f, 0.1f, 0.16f, 0.85f));
            Text(new Rect(x + 16, y + 6, w - 32, 32), _pitcherLine, _small, new Color(0.75f, 0.85f, 1f));
            Text(new Rect(x + 16, y + 40, w - 32, 32), _batterLine, _small, Color.white);
        }

        void DrawLog(float width, float height)
        {
            if (_log.Length == 0) return;
            const float w = 520;
            var h = 12 + _log.Length * 28;
            Fill(new Rect(width - w - 24, 20, w, h), new Color(0.05f, 0.1f, 0.16f, 0.8f));
            for (var i = 0; i < _log.Length; i++) Text(new Rect(width - w - 12, 26 + i * 28, w - 24, 28), _log[i], _small, new Color(1, 1, 1, 0.85f));
        }

        /// <summary>
        /// Hit test for the in-game pitch menu (screen pixels, y up as in Input.mousePosition). Returns true when the point is over
        /// the menu panel; <paramref name="index"/> is the item under it or -1 for the panel's header.
        /// </summary>
        public bool OverPitchMenu(Vector2 screen, out int index)
        {
            index = -1;
            if (_pitchItems.Length == 0) return false;
            var scale = Screen.height / 1080f;
            var p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            var height = Screen.height / scale;
            var h = 44 + _pitchItems.Length * 34;
            var y = (height - h) / 2;
            if (!new Rect(24, y, 300, h).Contains(p)) return false;
            for (var i = 0; i < _pitchItems.Length; i++)
                if (new Rect(30, y + 40 + i * 34, 288, 32).Contains(p)) { index = i; break; }
            return true;
        }

        void DrawPitchControls(float width, float height)
        {
            if (_pitchItems.Length > 0)
            {
                var h = 44 + _pitchItems.Length * 34;
                var y = (height - h) / 2;
                Fill(new Rect(24, y, 300, h), new Color(0.05f, 0.1f, 0.16f, 0.85f));
                Text(new Rect(36, y + 6, 270, 30), "구종 선택 (클릭 / 숫자키)", _small, new Color(1, 1, 1, 0.6f));
                for (var i = 0; i < _pitchItems.Length; i++)
                {
                    var selected = i == _pitchSelected;
                    if (selected) Fill(new Rect(30, y + 40 + i * 34, 288, 32), new Color(1f, 0.85f, 0.2f, 0.25f));
                    Text(new Rect(40, y + 40 + i * 34, 270, 32), $"{i + 1}  {_pitchItems[i]}", _small, selected ? new Color(1f, 0.9f, 0.4f) : Color.white);
                }
            }
            if (_gaugeVisible)
            {
                const float w = 520, h = 34;
                var x = (width - w) / 2; var y = height - 150;
                Fill(new Rect(x - 4, y - 28, w + 8, h + 36), new Color(0.05f, 0.1f, 0.16f, 0.85f));
                Text(new Rect(x, y - 26, w, 24), "릴리스: 가운데에서 놓으세요", _small, new Color(1, 1, 1, 0.7f));
                Fill(new Rect(x, y, w, h), new Color(1, 1, 1, 0.15f));
                Fill(new Rect(x + w * 0.45f, y, w * 0.10f, h), new Color(0.35f, 0.9f, 0.45f, 0.65f));   // sweet spot
                Fill(new Rect(x + w * Mathf.Clamp01(_gaugePos) - 3, y - 4, 6, h + 8), new Color(1f, 0.85f, 0.2f));
            }
        }

        void DrawBanner(float width)
        {
            if (Time.realtimeSinceStartup > _bannerUntil || _banner.Length == 0) return;
            var remaining = _bannerUntil - Time.realtimeSinceStartup;
            var alpha = Mathf.Clamp01(remaining * 2f);
            var c = _bannerColor; c.a = alpha;
            Text(new Rect(0, 150, width, 90), _banner, _banner1, c);
        }
    }
}
