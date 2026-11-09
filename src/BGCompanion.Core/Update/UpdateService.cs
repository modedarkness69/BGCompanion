using System.Net.Http.Headers;
using System.Text.Json;

namespace BGCompanion.Core.Update;

public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, string? AssetUrl, long AssetSize, string HtmlUrl);

/// <summary>Проверка и загрузка обновлений из релизов GitHub.</summary>
public static class UpdateService
{
    /// <summary>Вызывается перед повторной попыткой (номер попытки, ошибка).</summary>
    public static Action<int, Exception>? OnRetry { get; set; }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = Net.CreateClient(timeout);
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

    public static Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default) =>
        Net.RetryAsync(GetLatestOnceAsync, ct, onRetry: OnRetry);

    private static async Task<ReleaseInfo?> GetLatestOnceAsync(CancellationToken ct)
    {
        using var http = CreateClient(TimeSpan.FromSeconds(30));
        var url = $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest";
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // релизов ещё нет
        resp.EnsureSuccessStatusCode();
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

    public static Task DownloadAsync(string url, string target, IProgress<double>? progress, CancellationToken ct = default) =>
        Net.RetryAsync(async c => { await DownloadOnceAsync(url, target, progress, c); return true; }, ct, attempts: 5, onRetry: OnRetry);

    private static async Task DownloadOnceAsync(string url, string target, IProgress<double>? progress, CancellationToken ct)
    {
        using var http = Net.CreateClient(TimeSpan.FromMinutes(15));
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
        if (total > 0 && done != total) throw new IOException($"Скачано {done} из {total} байт");
    }
}
