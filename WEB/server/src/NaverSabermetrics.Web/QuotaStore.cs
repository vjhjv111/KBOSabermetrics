using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace NaverSabermetrics.Web;

/// <summary>Persistent per-IP daily budgets for the public record room.</summary>
public sealed class QuotaStore
{
    private readonly string _path;
    private readonly SiteOptions _options;
    private readonly byte[] _key;
    private readonly object _lock = new();
    public QuotaStore(SiteOptions options)
    {
        _options=options;
        Directory.CreateDirectory(options.StateDirectory);
        _path=Path.Combine(options.StateDirectory,"web_state.db");
        var keyPath=Path.Combine(options.StateDirectory,"quota.key");
        if(!File.Exists(keyPath))
        {
            using var stream=new FileStream(keyPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
            var bytes=RandomNumberGenerator.GetBytes(32);stream.Write(bytes);
        }
        _key=File.ReadAllBytes(keyPath);
        using var con=Open();using var cmd=con.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS Quotas(Day TEXT NOT NULL, Subject TEXT NOT NULL,
                Queries INTEGER NOT NULL, Rows INTEGER NOT NULL, PRIMARY KEY(Day,Subject));
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var con=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=_path,DefaultTimeout=3}.ToString());
        con.Open();return con;
    }
    public string HashIp(string ip) => Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes(ip)))[..24];
    public void Consume(string ip,int requestedRows)
    {
        if(requestedRows<0 || requestedRows>_options.MaxPageSize)throw new RequestError("출력량이 잘못되었습니다.");
        ConsumeBounded(ip,requestedRows);
    }
    // A league plot returns at most 500 season summaries. Reserve its whole
    // response budget once, without changing the record-page limit.
    public void ConsumeComparison(string ip) => ConsumeBounded(ip,500);

    private void ConsumeBounded(string ip,int requestedRows)
    {
        var day=DateTime.UtcNow.ToString("yyyy-MM-dd");
        lock(_lock)
        {
            using var con=Open();using var tx=con.BeginTransaction();
            foreach(var subject in new[]{"ip:"+HashIp(ip)})
            {
                using var get=con.CreateCommand();get.Transaction=tx;
                get.CommandText="SELECT Queries,Rows FROM Quotas WHERE Day=$day AND Subject=$subject";
                get.Parameters.AddWithValue("$day",day);get.Parameters.AddWithValue("$subject",subject);
                int queries=0, rows=0;
                using(var reader=get.ExecuteReader())if(reader.Read()){queries=reader.GetInt32(0);rows=reader.GetInt32(1);}
                if(queries+1>_options.DailyQueries || rows+requestedRows>_options.DailyRows)
                    throw new RequestError("오늘의 조회/열람 한도에 도달했습니다. UTC 날짜가 바뀐 뒤 다시 이용해 주세요.",429,"DAILY_QUOTA");
                using var save=con.CreateCommand();save.Transaction=tx;
                save.CommandText="""
                    INSERT INTO Quotas(Day,Subject,Queries,Rows) VALUES($day,$subject,1,$rows)
                    ON CONFLICT(Day,Subject) DO UPDATE SET Queries=Quotas.Queries+1,Rows=Quotas.Rows+$rows;
                    """;
                save.Parameters.AddWithValue("$day",day);save.Parameters.AddWithValue("$subject",subject);save.Parameters.AddWithValue("$rows",requestedRows);save.ExecuteNonQuery();
            }
            using var prune=con.CreateCommand();prune.Transaction=tx;
            prune.CommandText="DELETE FROM Quotas WHERE Day < $keep";
            prune.Parameters.AddWithValue("$keep",DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd"));prune.ExecuteNonQuery();
            tx.Commit();
        }
    }
}
