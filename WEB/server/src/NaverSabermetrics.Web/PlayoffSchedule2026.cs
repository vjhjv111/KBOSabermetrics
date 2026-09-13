namespace NaverSabermetrics.Web;

/// <summary>Preseason home/away allocation, including 45 initially undated games; valid only for 2026.</summary>
public static class PlayoffSchedule2026
{
    public const string Source = "https://www.koreabaseball.com/MediaNews/Notice/View.aspx?bdSe=11794";
    public const string AttachmentSha256 = "ff7f95e462affbe2ce5329fd864b1f94e099868b3a9c7873c63299237dff4e38";
    // Published 2025-12-19. Rows host, columns visit; 675 dated + 45 undated = 720.
    static readonly string[] Codes = ["HH", "HT", "KT", "LG", "LT", "NC", "OB", "SK", "SS", "WO"];
    static readonly int[,] Games = {
        { 0, 9, 7, 9, 7, 9, 7, 7, 7, 9 }, // HH
        { 7, 0, 9, 7, 9, 7, 9, 9, 9, 7 }, // HT
        { 9, 7, 0, 7, 9, 7, 9, 7, 7, 9 }, // KT
        { 7, 9, 9, 0, 7, 9, 7, 9, 9, 7 }, // LG
        { 9, 7, 7, 9, 0, 9, 7, 7, 7, 9 }, // LT
        { 7, 9, 9, 7, 7, 0, 9, 9, 9, 7 }, // NC
        { 9, 7, 7, 9, 9, 7, 0, 7, 7, 9 }, // OB
        { 9, 7, 9, 7, 9, 7, 9, 0, 7, 7 }, // SK
        { 9, 7, 9, 7, 9, 7, 9, 9, 0, 7 }, // SS
        { 7, 9, 7, 9, 7, 9, 7, 9, 9, 0 } // WO
    };

    public static int HomeGames(string home, string away)
    {
        int h=Array.IndexOf(Codes,home), a=Array.IndexOf(Codes,away);
        if(h<0||a<0)throw new ArgumentException("Unknown 2026 schedule team.");
        return Games[h,a];
    }
}
