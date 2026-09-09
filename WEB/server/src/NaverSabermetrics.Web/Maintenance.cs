using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NaverRelay.Infrastructure.Sqlite;
namespace NaverSabermetrics.Web;

public static class Maintenance
{
    public static PasswordHasher<WebUser> Hasher() => new(Options.Create(new PasswordHasherOptions { IterationCount=210000 }));
    public static bool IsCommand(string x) => new[]{"--hash","--prepare","--sample-db","--check-db","--compare","--self-test","--http-check","--http-check-rate","--help"}.Contains(x);
    public static async Task<bool> ExecuteAsync(string[] args)
    {
        if(args.Length==0 || !IsCommand(args[0]))return false;
        switch(args[0])
        {
            case "--hash":
                var password=Environment.GetEnvironmentVariable("SABER_PASSWORD");
                if(password is null)
                {
                    Console.Error.Write("Password (12+ chars, hidden): ");
                    if(Console.IsInputRedirected)password=Console.ReadLine()??"";
                    else
                    {
                        var text=new System.Text.StringBuilder();
                        while(true){var k=Console.ReadKey(true);if(k.Key==ConsoleKey.Enter)break;
                            if(k.Key==ConsoleKey.Backspace){if(text.Length>0)text.Length--;}
                            else if(!char.IsControl(k.KeyChar))text.Append(k.KeyChar);}
                        password=text.ToString();Console.Error.WriteLine();
                    }
                }
                if(password.Length<12 || password.Length>256)throw new ArgumentException("Use a password between 12 and 256 characters.");
                Console.WriteLine(Hasher().HashPassword(new WebUser(),password));break;
            case "--prepare":
                Need(args,3,"--prepare <desktop.db> <NEW web-readonly.db>");
                await PrepareAsync(args[1],args[2]);break;
            case "--sample-db":
                Need(args,3,"--sample-db <SampleData/2026.zip> <NEW sample.db>");
                await Verification.CreateSampleDatabaseAsync(args[1],args[2]);break;
            case "--check-db":
                Need(args,2,"--check-db <web-copy.db>");await Verification.AuditDatabaseAsync(args[1]);break;
            case "--compare":
                Need(args,2,"--compare <web-copy.db>");await Verification.CompareDesktopAndWebAsync(args[1]);break;
            case "--self-test":
                Need(args,2,"--self-test <sample.db>");await Verification.SelfTestAsync(args[1]);break;
            case "--http-check": case "--http-check-rate":
                Need(args,2,"--http-check <http://127.0.0.1:port>");
                await Verification.HttpCheckAsync(args[1],args[0]=="--http-check-rate");break;
            default:
                Console.WriteLine("CLI: --hash | --prepare SOURCE NEW_DEST | --sample-db ZIP NEW_DB | --check-db DB | --compare DB | --self-test SAMPLE_DB | --http-check LOOPBACK_URL | --http-check-rate LOOPBACK_URL");break;
        }
        return true;
    }
    private static void Need(string[] args,int count,string usage)
    { if(args.Length!=count)throw new ArgumentException("Usage: "+usage); }

    public static async Task PrepareAsync(string source,string destination)
    {
        source=Path.GetFullPath(source); destination=Path.GetFullPath(destination);
        if(!File.Exists(source))throw new FileNotFoundException("Source DB does not exist",source);
        if(string.Equals(source,destination,StringComparison.OrdinalIgnoreCase)||File.Exists(destination))
            throw new IOException("Destination must be a NEW file, different from the source. No overwrite is performed.");
        await new DatabaseCacheService(source,webReadOnly:true).ValidateWebSchemaAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary=destination+".preparing-"+Guid.NewGuid().ToString("N");
        try
        {
            Console.WriteLine("Backing up source into a separate web snapshot. Source is read-only.");
            using(var from=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var to=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString()))
            { from.Open();to.Open();from.BackupDatabase(to); }
            var db=new DatabaseCacheService(temporary);
            await db.InitializeAsync(); // DDL/indexes only in the NEW copy.
            Console.WriteLine("Warming uploaded V3 league constants and calibration on the new copy...");
            _=await db.GetLeagueReferenceAsync();
            SqliteConnection.ClearAllPools();
            using(var con=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString()))
            {
                con.Open();using var cmd=con.CreateCommand();cmd.CommandTimeout=600;
                cmd.CommandText="ANALYZE; PRAGMA optimize;";cmd.ExecuteNonQuery();
            }
            Verification.SealSnapshot(temporary);
            File.Move(temporary,destination);
            Console.WriteLine("PREPARED_DB="+destination);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            foreach(var p in new[]{temporary,temporary+"-wal",temporary+"-shm"})if(File.Exists(p))File.Delete(p);
            throw;
        }
    }
}
