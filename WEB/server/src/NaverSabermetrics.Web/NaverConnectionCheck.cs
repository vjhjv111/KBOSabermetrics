using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
namespace NaverSabermetrics.Web;
public static class NaverConnectionCheck
{
    public static async Task RunAsync(string id)
    {
        if(!Regex.IsMatch(id,@"^\d{8}[A-Z]{4}\d{5}$"))throw new ArgumentException("Expected 17-character game ID.");
        const string host="api-gw.sports.naver.com";
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var clock=Stopwatch.StartNew();
        var stage="DNS";
        try {
            var addresses=await Dns.GetHostAddressesAsync(host,deadline.Token);
            Console.WriteLine($"DNS {clock.ElapsedMilliseconds}ms: {string.Join(", ",addresses.Select(a=>a.ToString()))}");
            stage="TCP";
            using var tcp=new TcpClient();
            await tcp.ConnectAsync(host,443,deadline.Token);
            Console.WriteLine($"TCP {clock.ElapsedMilliseconds}ms peer={tcp.Client.RemoteEndPoint}");
            stage="TLS";
            using var tls=new SslStream(tcp.GetStream(),false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {TargetHost=host},deadline.Token);
            Console.WriteLine($"TLS {clock.ElapsedMilliseconds}ms protocol={tls.SslProtocol}");
            stage="HTTP headers";
            var request=$"GET /schedule/games/{id}/game-polling?isHighlight=false HTTP/1.1\r\nHost: {host}\r\nUser-Agent: Mozilla/5.0 (compatible; KBOSabermetrics-RenderCollector/1.0)\r\nConnection: close\r\n\r\n";
            await tls.WriteAsync(Encoding.ASCII.GetBytes(request),deadline.Token);
            await tls.FlushAsync(deadline.Token);
            using var reader=new StreamReader(tls);
            var status=await reader.ReadLineAsync(deadline.Token);
            Console.WriteLine($"HTTP {clock.ElapsedMilliseconds}ms: {status??"(connection closed)"}");
            Environment.ExitCode=status?.Contains(" 200 ")==true?0:1;
        } catch(Exception ex) when(ex is IOException or SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException) {
            Console.WriteLine($"FAIL stage={stage} elapsed={clock.ElapsedMilliseconds}ms {ex.GetType().Name}: {ex.Message}");
            Environment.ExitCode=1;
        }
    }
}
