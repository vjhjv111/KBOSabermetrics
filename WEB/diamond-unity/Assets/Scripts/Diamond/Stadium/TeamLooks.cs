using UnityEngine;

namespace Diamond.Stadium
{
    /// <summary>Club colours (primary, secondary) matched by name, and the home/away uniform built from them.</summary>
    public static class TeamLooks
    {
        static readonly (string key, Color primary, Color secondary)[] Table =
        {
            ("LG", new Color(0.78f, 0.06f, 0.18f), new Color(0.08f, 0.08f, 0.08f)),
            ("KIA", new Color(0.8f, 0.1f, 0.12f), new Color(0.08f, 0.08f, 0.08f)),
            ("삼성", new Color(0.05f, 0.3f, 0.65f), new Color(0.85f, 0.85f, 0.9f)),
            ("두산", new Color(0.05f, 0.08f, 0.25f), new Color(0.8f, 0.1f, 0.12f)),
            ("롯데", new Color(0.05f, 0.1f, 0.28f), new Color(0.8f, 0.1f, 0.15f)),
            ("한화", new Color(0.95f, 0.45f, 0.05f), new Color(0.08f, 0.08f, 0.1f)),
            ("SSG", new Color(0.8f, 0.1f, 0.15f), new Color(0.95f, 0.75f, 0.1f)),
            ("NC", new Color(0.06f, 0.15f, 0.35f), new Color(0.75f, 0.6f, 0.25f)),
            ("KT", new Color(0.08f, 0.08f, 0.08f), new Color(0.8f, 0.1f, 0.15f)),
            ("키움", new Color(0.45f, 0.05f, 0.15f), new Color(0.85f, 0.85f, 0.85f)),
        };

        public static PlayerKit.Look For(string team, bool home)
        {
            var primary = new Color(0.15f, 0.2f, 0.45f);
            if (!string.IsNullOrEmpty(team))
                foreach (var t in Table) if (team.Contains(t.key)) { primary = t.primary; break; }
            var white = new Color(0.93f, 0.93f, 0.93f); var grey = new Color(0.68f, 0.7f, 0.74f);
            return new PlayerKit.Look
            {
                jersey = home ? white : grey, pants = home ? white : grey, sleeve = primary, socks = primary, cap = primary,
            };
        }
    }
}
