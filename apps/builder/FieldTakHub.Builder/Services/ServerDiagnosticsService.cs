using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using FieldTakHub.Builder.Models;

namespace FieldTakHub.Builder.Services;

public sealed record ServerTestResult(string Name, bool Success, string Detail);

public sealed class ServerDiagnosticsService
{
    public async Task<IReadOnlyList<ServerTestResult>> TestAsync(ServerProfile server, CancellationToken ct = default)
    {
        ServerValidator.Validate(server);
        var results = new List<ServerTestResult>();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(server.Host, ct);
            results.Add(new("DNS", addresses.Length > 0, addresses.Length > 0 ? string.Join(", ", addresses.Select(x => x.ToString())) : "No address returned"));
        }
        catch (Exception ex) { results.Add(new("DNS", false, ex.Message)); }

        results.Add(await TcpAsync("CoT", server.Host, server.CotPort, ct));
        results.Add(await TcpAsync("Enrollment/API", server.Host, server.ApiPort, ct));
        results.Add(await TcpAsync("Web", server.Host, server.WebPort, ct));
        results.Add(await TlsAsync(server.Host, server.WebPort, ct));
        return results;
    }

    private static async Task<ServerTestResult> TcpAsync(string name, string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await client.ConnectAsync(host, port, timeout.Token);
            return new(name, true, $"TCP {port} reachable");
        }
        catch (Exception ex) { return new(name, false, $"TCP {port}: {ex.Message}"); }
    }

    private static async Task<ServerTestResult> TlsAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(host, port, timeout.Token);
            X509Certificate2? cert = null;
            using var ssl = new SslStream(tcp.GetStream(), false, (_, certificate, _, errors) => { if (certificate != null) cert = new X509Certificate2(certificate); return errors == SslPolicyErrors.None; });
            await ssl.AuthenticateAsClientAsync(host);
            var detail = cert is null ? "TLS handshake OK" : $"TLS OK • {cert.Subject} • expires {cert.NotAfter:yyyy-MM-dd}";
            return new("TLS", true, detail);
        }
        catch (Exception ex) { return new("TLS", false, ex.Message); }
    }
}
