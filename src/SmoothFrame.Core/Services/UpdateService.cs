using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothFrame.Core.Services;

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "v3.0.0";
    public string LatestVersion { get; set; } = "";
    public string ReleaseName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string HtmlUrl { get; set; } = "https://github.com/Foxys1/SmoothFrame/releases";
    public string? ErrorMessage { get; set; }
}

public static class UpdateService
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private const string GitHubApiReleasesUrl = "https://api.github.com/repos/Foxys1/SmoothFrame/releases/latest";
    private const string DefaultReleasesWebUrl = "https://github.com/Foxys1/SmoothFrame/releases";

    public static Version GetCurrentVersion()
    {
        var asmVer = Assembly.GetEntryAssembly()?.GetName().Version 
                     ?? typeof(UpdateService).Assembly.GetName().Version;
        if (asmVer == null) return new Version(3, 0, 0, 0);
        return new Version(
            Math.Max(0, asmVer.Major),
            Math.Max(0, asmVer.Minor),
            Math.Max(0, asmVer.Build),
            Math.Max(0, asmVer.Revision));
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var curVer = GetCurrentVersion();
        var curVerStr = $"v{curVer.Major}.{curVer.Minor}.{curVer.Build}";

        var result = new UpdateCheckResult
        {
            CurrentVersion = curVerStr,
            HtmlUrl = DefaultReleasesWebUrl
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiReleasesUrl);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SmoothFrame-App", "3.0.0"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                result.ErrorMessage = $"HTTP {(int)resp.StatusCode}";
                return result;
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string name = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : DefaultReleasesWebUrl;

            result.LatestVersion = tag;
            result.ReleaseName = name;
            result.ReleaseNotes = CleanMarkdown(body);
            result.HtmlUrl = string.IsNullOrWhiteSpace(htmlUrl) ? DefaultReleasesWebUrl : htmlUrl;

            if (TryParseVersion(tag, out var latestVer))
            {
                if (latestVer > curVer)
                {
                    result.IsUpdateAvailable = true;
                }
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private static string CleanMarkdown(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        // Clean leading/trailing whitespaces and excessive line breaks for UI preview
        var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var preview = lines.Length > 0 ? lines[0] : "";
        if (lines.Length > 1 && preview.Length < 100)
        {
            preview += " " + lines[1];
        }
        return preview.Trim();
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        var clean = tag.Trim().TrimStart('v', 'V');
        var dashIndex = clean.IndexOf('-');
        if (dashIndex > 0)
        {
            clean = clean.Substring(0, dashIndex);
        }

        if (Version.TryParse(clean, out var parsed) && parsed != null)
        {
            int major = Math.Max(0, parsed.Major);
            int minor = Math.Max(0, parsed.Minor);
            int build = Math.Max(0, parsed.Build);
            int revision = Math.Max(0, parsed.Revision);
            version = new Version(major, minor, build, revision);
            return true;
        }

        return false;
    }
}
