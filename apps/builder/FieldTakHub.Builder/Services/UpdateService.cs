using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace FieldTakHub.Builder.Services;

public sealed record BuilderUpdateInfo(string Version, string AssetName, string AssetUrl, string Sha256, string ReleasePage, string Channel);

public sealed class UpdateService
{
    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly string _channel;

    public UpdateService()
    {
        var cfgPath = Path.Combine(AppContext.BaseDirectory, "update-config.json");
        var repo = "Qbcol/TAKFieldHub"; var channel = "rc";
        if (File.Exists(cfgPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(cfgPath));
            repo = doc.RootElement.TryGetProperty("repository", out var r) ? r.GetString() ?? repo : repo;
            channel = doc.RootElement.TryGetProperty("channel", out var c) ? c.GetString() ?? channel : channel;
        }
        _repository = repo; _channel = channel;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FieldTAKHubBuilder", "2.2.0-rc1"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<BuilderUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_repository) || !_repository.Contains('/')) return null;
        var releasesJson = await GetHttpsTextAsync($"https://api.github.com/repos/{_repository}/releases?per_page=20", ct);
        using var releases = JsonDocument.Parse(releasesJson);
        JsonElement? chosen = null;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var prerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if ((_channel.Equals("rc", StringComparison.OrdinalIgnoreCase) && prerelease) || (_channel.Equals("stable", StringComparison.OrdinalIgnoreCase) && !prerelease)) { chosen = release.Clone(); break; }
        }
        if (chosen is null) return null;
        var selected = chosen.Value; var assets = selected.GetProperty("assets").EnumerateArray().Select(a=>a.Clone()).ToList();
        var manifestAsset = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "fieldtak-release.json");
        if (manifestAsset.ValueKind == JsonValueKind.Undefined) return null;
        var manifestUrl = manifestAsset.GetProperty("browser_download_url").GetString();
        if (string.IsNullOrWhiteSpace(manifestUrl)) return null;
        using var manifest = JsonDocument.Parse(await GetHttpsTextAsync(manifestUrl, ct));
        if (!string.Equals(manifest.RootElement.GetProperty("channel").GetString(), _channel, StringComparison.OrdinalIgnoreCase)) return null;
        var builder = manifest.RootElement.GetProperty("builder");
        var version = builder.GetProperty("version").GetString() ?? "";
        if (CompareVersions(version, "2.2.0-rc1") <= 0) return null;
        var assetName = builder.GetProperty("asset").GetString() ?? "";
        var sha = builder.GetProperty("sha256").GetString()?.ToLowerInvariant() ?? "";
        var match = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == assetName);
        var url = match.ValueKind == JsonValueKind.Undefined ? null : match.GetProperty("browser_download_url").GetString();
        if (string.IsNullOrWhiteSpace(url) || sha.Length != 64) return null;
        return new(version, assetName, url, sha, selected.GetProperty("html_url").GetString() ?? "", _channel);
    }

    public async Task<string> DownloadAsync(BuilderUpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var target = Path.Combine(Path.GetTempPath(), update.AssetName);
        using var response = await SendHttpsAsync(update.AssetUrl, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[128 * 1024]; long current = 0;
        while (true)
        {
            var n = await input.ReadAsync(buffer, ct); if (n <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, n), ct); current += n;
            if (total is > 0) progress?.Report((double)current / total.Value);
        }
        await using var hashStream = File.OpenRead(target);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, ct)).ToLowerInvariant();
        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("FTH-UPD-104: downloaded Builder SHA-256 mismatch.");
        return target;
    }

    public static void ShowInExplorer(string file) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });

    private async Task<string> GetHttpsTextAsync(string url, CancellationToken ct)
    {
        using var response = await SendHttpsAsync(url, ct); response.EnsureSuccessStatusCode(); return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<HttpResponseMessage> SendHttpsAsync(string initial, CancellationToken ct)
    {
        var current = RequireHttps(initial);
        for (var i = 0; i < 6; i++)
        {
            var response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("FTH-UPD-101: redirect without Location.");
                current = RequireHttps(location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(current), location).ToString());
                response.Dispose(); continue;
            }
            return response;
        }
        throw new InvalidDataException("FTH-UPD-102: too many redirects.");
    }

    private static string RequireHttps(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("FTH-UPD-103: update URL must use HTTPS.");
        return uri.ToString();
    }

    private static int CompareVersions(string a, string b)
    {
        static int[] V(string x) => x.Split('-', 2)[0].Split('.').Select(v => int.TryParse(v, out var n) ? n : 0).Concat(Enumerable.Repeat(0, 4)).Take(4).ToArray();
        var av=V(a); var bv=V(b); for(var i=0;i<4;i++){var c=av[i].CompareTo(bv[i]);if(c!=0)return c;} return string.Compare(a,b,StringComparison.OrdinalIgnoreCase);
    }
}
