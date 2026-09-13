namespace NaverSabermetrics.Web;

public sealed class SiteOptions
{
    public string DatabasePath { get; set; } = "";
    public string PlayerPhotoDirectory { get; set; } = "";
    public string StateDirectory { get; set; } = "App_Data";
    public bool Demo { get; set; }
    public bool AllowDevelopmentGuest { get; set; }
    public bool ShowWar { get; set; } = true;
    public int MaxPageSize { get; set; } = 50;
    public int MaxAccessibleRows { get; set; } = 1000;
    public int RequestsPerMinute { get; set; } = 30;
    public int IpRequestsPerMinute { get; set; } = 90;
    public int DailyQueries { get; set; } = 300;
    public int DailyRows { get; set; } = 10000;
    public int ConcurrentQueries { get; set; } = 2;
    public int QuerySeconds { get; set; } = 30;
    public string[] TrustedProxies { get; set; } = Array.Empty<string>();
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public List<WebUser> Users { get; set; } = new();
}
public sealed class WebUser
{
    public string Id { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
}
public sealed class RequestError : Exception
{
    public int Status { get; }
    public string Code { get; }
    public RequestError(string message, int status = 400, string code = "INVALID_QUERY") : base(message)
    { Status=status; Code=code; }
}
