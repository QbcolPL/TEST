using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FieldTakHub.Builder.Services;

public sealed class DistributionServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _packagePath;
    private string? _packageSha256;
    private DateTime _expiresUtc;
    private string? _token;
    private int _port;
    private string _advertisedIp = "127.0.0.1";
    private int _downloadCount;
    private int _maxDownloads = 50;
    private long _recommendedFreeBytes;

    public string? ProvisioningUrl { get; private set; }
    public int DownloadCount => Volatile.Read(ref _downloadCount);

    public string Start(string packagePath, TimeSpan validFor, int maxDownloads = 50, int port = 8765)
    {
        Stop();
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Package not found.", packagePath);
        _packagePath = packagePath;
        using (var hashStream = File.OpenRead(packagePath)) _packageSha256 = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
        _recommendedFreeBytes = ReadRecommendedFreeBytes(packagePath);
        _expiresUtc = DateTime.UtcNow.Add(validFor);
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
        _port = port;
        _maxDownloads = Math.Max(1, maxDownloads);
        _advertisedIp = GetLanIp();
        Interlocked.Exchange(ref _downloadCount, 0);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        ProvisioningUrl = $"http://{_advertisedIp}:{port}/d/{_token}.json";
        _ = RunAsync(_cts.Token);
        return ProvisioningUrl;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null; _cts?.Dispose(); _cts = null; ProvisioningUrl = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { continue; }
            _ = Task.Run(() => HandleAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            await using var stream = client.GetStream();
            try
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 8192, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(requestLine)) return;
                var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) break;
                    var colon = line.IndexOf(':');
                    if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase)) { await WriteStatus(stream, 405, "Method Not Allowed", ct); return; }
                if (_token is null || _packagePath is null || DateTime.UtcNow > _expiresUtc) { await WriteStatus(stream, 410, "Gone", ct); return; }

                var requestPath = Uri.UnescapeDataString(parts[1].Split('?', 2)[0]);
                var descriptorPath = $"/d/{_token}.json";
                var packageRoute = $"/p/{_token}/{Path.GetFileName(_packagePath)}";

                if (string.Equals(requestPath, descriptorPath, StringComparison.Ordinal))
                {
                    var packageUrl = $"http://{_advertisedIp}:{_port}/p/{Uri.EscapeDataString(_token)}/{Uri.EscapeDataString(Path.GetFileName(_packagePath))}";
                    var body = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        schema = "fieldtak.provision", version = 1, packageUrl,
                        packageSha256 = _packageSha256,
                        label = Path.GetFileNameWithoutExtension(_packagePath),
                        expiresUtc = _expiresUtc, maxDownloads = _maxDownloads,
                        packageBytes = new FileInfo(_packagePath).Length,
                        recommendedFreeBytes = _recommendedFreeBytes > 0 ? (long?)_recommendedFreeBytes : null
                    }, JsonDefaults.Options);
                    await WriteBytes(stream, 200, "OK", "application/json; charset=utf-8", body, null, null, ct);
                }
                else if (string.Equals(requestPath, packageRoute, StringComparison.Ordinal))
                {
                    var info = new FileInfo(_packagePath);
                    var (start,end,partial) = ParseRange(headers.TryGetValue("Range",out var range) ? range : null, info.Length);
                    if (!partial)
                    {
                        if (DownloadCount >= _maxDownloads) { await WriteStatus(stream, 429, "Download Limit Reached", ct); return; }
                        Interlocked.Increment(ref _downloadCount);
                    }
                    var length = end - start + 1;
                    var extra = new Dictionary<string,string> { ["Accept-Ranges"] = "bytes" };
                    if (partial) extra["Content-Range"] = $"bytes {start}-{end}/{info.Length}";
                    var header = BuildHeader(partial ? 206 : 200, partial ? "Partial Content" : "OK", "application/vnd.fieldtak.package", length,
                        $"attachment; filename=\"{Path.GetFileName(_packagePath).Replace("\"", string.Empty)}\"", extra);
                    await stream.WriteAsync(header, ct);
                    await using var fs = File.OpenRead(_packagePath);
                    fs.Seek(start, SeekOrigin.Begin);
                    var remaining = length; var buffer = new byte[128 * 1024];
                    while (remaining > 0)
                    {
                        var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                        if (read <= 0) break;
                        await stream.WriteAsync(buffer.AsMemory(0, read), ct); remaining -= read;
                    }
                }
                else await WriteStatus(stream, 404, "Not Found", ct);
            }
            catch { /* best effort LAN server */ }
        }
    }

    private static (long Start,long End,bool Partial) ParseRange(string? range,long length)
    {
        if (string.IsNullOrWhiteSpace(range) || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return (0,length-1,false);
        var first = range[6..].Split(',',2)[0].Trim(); var pair = first.Split('-',2);
        if (pair.Length != 2 || !long.TryParse(pair[0], out var start) || start < 0 || start >= length) return (0,length-1,false);
        var end = long.TryParse(pair[1], out var parsed) ? Math.Min(parsed,length-1) : length-1;
        if (end < start) return (0,length-1,false);
        return (start,end,true);
    }

    private static async Task WriteStatus(Stream stream, int code, string reason, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes($"{code} {reason}\n");
        await WriteBytes(stream, code, reason, "text/plain; charset=utf-8", body, null, null, ct);
    }

    private static async Task WriteBytes(Stream stream, int code, string reason, string contentType, byte[] body, string? disposition, Dictionary<string,string>? extra, CancellationToken ct)
    {
        var header = BuildHeader(code, reason, contentType, body.LongLength, disposition, extra);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(body, ct);
    }

    private static byte[] BuildHeader(int code, string reason, string contentType, long length, string? disposition, Dictionary<string,string>? extra)
    {
        var sb = new StringBuilder().Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n")
            .Append("Connection: close\r\n").Append("Cache-Control: no-store\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n").Append("Content-Length: ").Append(length).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(disposition)) sb.Append("Content-Disposition: ").Append(disposition).Append("\r\n");
        if (extra is not null) foreach (var h in extra) sb.Append(h.Key).Append(": ").Append(h.Value).Append("\r\n");
        sb.Append("\r\n"); return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static long ReadRecommendedFreeBytes(string packagePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var entry = zip.GetEntry("META-INF/fieldtak.json");
            if (entry is null) return 0;
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("size", out var size) &&
                size.TryGetProperty("recommendedFreeBytes", out var value) &&
                value.TryGetInt64(out var bytes)) return bytes;
        }
        catch { }
        return 0;
    }

    private static string GetLanIp()
    {
        try { using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp); socket.Connect("8.8.8.8",65530); if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString(); } catch { }
        return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a))?.ToString() ?? "127.0.0.1";
    }

    public void Dispose()=>Stop();
}
