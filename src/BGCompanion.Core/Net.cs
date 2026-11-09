using System.Net;
using System.Net.Sockets;

namespace BGCompanion.Core;

/// <summary>
/// HTTP для нестабильных сетей: сначала IPv4 (IPv6 у многих провайдеров не работает),
/// короткий таймаут на подключение и повторные попытки.
/// </summary>
public static class Net
{
    public static HttpClient CreateClient(TimeSpan timeout, bool autoRedirect = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = autoRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            ConnectCallback = ConnectPreferIPv4,
        };
        var http = new HttpClient(handler) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BGCompanion/{AppInfo.VersionText}");
        return http;
    }

    private static async ValueTask<Stream> ConnectPreferIPv4(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
        Exception? last = null;
        foreach (var addr in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6 ? 1 : 0))
        {
            var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                await socket.ConnectAsync(addr, ctx.DnsEndPoint.Port, cts.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
                socket.Dispose();
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    /// <summary>Выполнить сетевую операцию с повторами (по умолчанию 4 попытки).</summary>
    public static async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct = default,
        int attempts = 4, Action<int, Exception>? onRetry = null)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (i < attempts && !ct.IsCancellationRequested && IsTransient(ex))
            {
                onRetry?.Invoke(i, ex);
                await Task.Delay(TimeSpan.FromSeconds(2 * i), ct);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException h => h.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or >= HttpStatusCode.InternalServerError,
        TaskCanceledException or TimeoutException or IOException or SocketException => true,
        _ => ex.InnerException != null && IsTransient(ex.InnerException),
    };
}
