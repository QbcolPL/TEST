using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FieldTakHub.Builder.Services;

public sealed record ResolvedCloudUrl(string InputUrl, string DownloadUrl, string Provider, bool WasNormalized, string? FileId = null);
public sealed record CloudLinkTestResult(bool Success, int StatusCode, string FinalUrl, string ContentType, long? ContentLength, string Detail, string DownloadUrl, string Provider, bool WasNormalized);

/// <summary>
/// Creates direct-cloud provisioning QR links. Builder never uploads the package itself:
/// the operator uploads .ftak to an HTTPS origin, pastes either a direct file URL or a
/// supported sharing URL (currently Google Drive), tests it, and Builder binds the normalized
/// download URL to the local package SHA-256 inside the QR deep-link.
/// </summary>
public sealed class CloudDistributionService : IDisposable
{
    private readonly HttpClient _http;

    public CloudDistributionService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 8 };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FieldTAKHubBuilder", "2.2.0-rc1"));
    }

    public static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidDataException("FTH-CLOUD-001: external/cloud package URL must be an absolute HTTPS URL.");
        return uri;
    }

    /// <summary>
    /// Accepts a normal direct HTTPS URL or a common Google Drive sharing URL.
    /// Google Drive file links are converted to a stable direct-download endpoint before
    /// testing and before embedding the URL in a provisioning QR. Folder links are rejected.
    /// </summary>
    public static ResolvedCloudUrl ResolvePackageUrl(string value)
    {
        var input = RequireHttps(value).ToString();
        var uri = new Uri(input);
        var host = uri.Host.ToLowerInvariant();

        if (host == "drive.usercontent.google.com")
            return new ResolvedCloudUrl(input, input, "Google Drive", false, GetQueryParameter(uri, "id"));

        if (host == "drive.google.com" || host.EndsWith(".drive.google.com", StringComparison.Ordinal))
        {
            if (uri.AbsolutePath.Contains("/folders/", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.StartsWith("/drive/folders", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("FTH-CLOUD-003: Google Drive folder links are not supported. Share the individual .ftak file and paste its file link.");

            string? fileId = null;
            var m = Regex.Match(uri.AbsolutePath, @"/file/d/([^/]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success) fileId = Uri.UnescapeDataString(m.Groups[1].Value);
            fileId ??= GetQueryParameter(uri, "id");

            if (string.IsNullOrWhiteSpace(fileId))
                throw new InvalidDataException("FTH-CLOUD-004: this Google Drive URL does not contain a file ID. Use Share → Copy link for the individual .ftak file.");

            var resourceKey = GetQueryParameter(uri, "resourcekey");
            var direct = "https://drive.usercontent.google.com/download?id=" + Uri.EscapeDataString(fileId) + "&export=download&confirm=t";
            if (!string.IsNullOrWhiteSpace(resourceKey))
                direct += "&resourcekey=" + Uri.EscapeDataString(resourceKey);

            return new ResolvedCloudUrl(input, direct, "Google Drive", true, fileId);
        }

        return new ResolvedCloudUrl(input, input, "HTTPS", false);
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        var q = uri.Query;
        if (string.IsNullOrWhiteSpace(q)) return null;
        if (q.StartsWith("?", StringComparison.Ordinal)) q = q[1..];
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (!Uri.UnescapeDataString(kv[0]).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return kv.Length == 2 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
        }
        return null;
    }

    public async Task<CloudLinkTestResult> TestAsync(string url, CancellationToken cancellationToken = default)
    {
        var resolved = ResolvePackageUrl(url);
        var uri = new Uri(resolved.DownloadUrl);
        HttpResponseMessage? response = null;
        try
        {
            using (var head = new HttpRequestMessage(HttpMethod.Head, uri))
            {
                response = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            // Some object stores/CDNs (including Google Drive) reject HEAD. Probe one byte
            // instead without downloading the whole package.
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                using var get = new HttpRequestMessage(HttpMethod.Get, uri);
                get.Headers.Range = new RangeHeaderValue(0, 0);
                response = await _http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }

            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString();
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var len = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;
            var html = mediaType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
            var finalHttps = Uri.TryCreate(finalUrl, UriKind.Absolute, out var finalUri) && finalUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var ok = response.IsSuccessStatusCode && !html && finalHttps;

            string detail;
            if (resolved.Provider == "Google Drive" && (!response.IsSuccessStatusCode || html))
            {
                detail = $"Google Drive did not return package bytes (HTTP {(int)response.StatusCode}, type={(string.IsNullOrWhiteSpace(mediaType) ? "unknown" : mediaType)}). " +
                         "Set the .ftak file to General access: Anyone with the link / Viewer, then paste the file sharing link again. Folder links do not work.";
            }
            else if (html)
            {
                detail = "The URL returned HTML instead of package bytes. Use a direct/download link to the .ftak file.";
            }
            else
            {
                var normalized = resolved.WasNormalized ? $"; normalized={resolved.DownloadUrl}" : string.Empty;
                detail = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}; provider={resolved.Provider}; type={(string.IsNullOrWhiteSpace(mediaType) ? "unknown" : mediaType)}; final={finalUrl}{normalized}";
            }

            return new CloudLinkTestResult(ok, (int)response.StatusCode, finalUrl, mediaType, len, detail, resolved.DownloadUrl, resolved.Provider, resolved.WasNormalized);
        }
        finally { response?.Dispose(); }
    }

    public static string Sha256File(string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Local .ftak file not found.", file);
        using var input = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    public static string CreateDeepLink(string packageUrl, string localPackage, DateTimeOffset expiresUtc, string label)
    {
        var resolved = ResolvePackageUrl(packageUrl);
        var uri = RequireHttps(resolved.DownloadUrl);
        if (!File.Exists(localPackage)) throw new FileNotFoundException("Local .ftak file not found.", localPackage);
        if (!Path.GetExtension(localPackage).Equals(".ftak", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FTH-CLOUD-002: select the exact local .ftak file that was uploaded to the cloud.");

        var sha = Sha256File(localPackage);
        var bytes = new FileInfo(localPackage).Length;
        var query = string.Join("&", new[]
        {
            "packageUrl=" + Uri.EscapeDataString(uri.ToString()),
            "sha256=" + Uri.EscapeDataString(sha),
            "packageBytes=" + bytes,
            "expiresUtc=" + Uri.EscapeDataString(expiresUtc.UtcDateTime.ToString("O")),
            "label=" + Uri.EscapeDataString(label ?? string.Empty)
        });
        return "fieldtak://provision?" + query;
    }

    public void Dispose() => _http.Dispose();
}
