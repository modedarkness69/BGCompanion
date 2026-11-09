using System.Net.Http.Headers;
using System.Text.Json;

namespace BGCompanion.Core.Update;

public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, string? AssetUrl, long AssetSize, string HtmlUrl);

/// <summary>Проверка и загрузка обновлений из релизов GitHub.</summary>
public static class UpdateService
{
    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BGCompanion/{AppInfo.VersionText}");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    public static Version? ParseTag(string tag)
    {
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash >= 0) t = t[..dash];
        return Version.TryParse(t, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var http = CreateClient();
        var url = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var ver = ParseTag(tag);
        if (ver == null) return null;
        string? assetUrl = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), AppInfo.ReleaseAsset, StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    size = a.GetProperty("size").GetInt64();
                }
            }
        }
        return new ReleaseInfo(ver, tag,
            root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            assetUrl, size,
            root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "");
    }

    public static bool IsNewer(ReleaseInfo r) => r.Version > Normalize(AppInfo.CurrentVersion);

    public static async Task DownloadAsync(string url, string target, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var http = CreateClient();
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(target);
        var buf = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            done += n;
            if (total > 0) progress?.Report((double)done / total);
        }
    }
}
