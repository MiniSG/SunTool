using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SunTool.Modules.NetScan.Logic;

/// <summary>
/// 범위 안의 IPv4 주소에 핑(ICMP echo)을 보내 응답하는 장치를 찾습니다. 포트를 열거나 페이로드를
/// 보내지 않는, <c>ping</c> · <c>arp -a</c> 수준의 LAN 목록 작성 도구입니다. 동시 실행 수를 제한하고
/// CancellationToken 으로 즉시 멈출 수 있습니다.
/// </summary>
public sealed class PingScanner
{
    private const int DefaultTimeoutMs = 1000;
    private static readonly byte[] PingPayload = new byte[32]; // 표준 ping 과 같은 크기의 빈 버퍼

    public int TimeoutMs { get; init; } = DefaultTimeoutMs;

    /// <summary>역방향 DNS 로 이름을 찾을지 (느릴 수 있어 끌 수 있음).</summary>
    public bool ResolveNames { get; init; } = true;

    /// <summary>
    /// 범위를 훑습니다. 응답한 장치마다 <paramref name="onFound"/> 를, 주소 하나를 끝낼 때마다
    /// <paramref name="onProgress"/>(완료 수) 를 호출합니다. 콜백은 임의의 스레드에서 불립니다.
    /// </summary>
    public async Task ScanAsync(
        IpRange range,
        IReadOnlySet<uint> exclude,
        int maxConcurrency,
        IReadOnlyDictionary<string, string>? arpFallback,
        Action<ScanRow> onFound,
        Action<int> onProgress,
        CancellationToken token)
    {
        var concurrency = Math.Clamp(maxConcurrency, 1, 1024);
        using var gate = new SemaphoreSlim(concurrency);
        var done = 0;
        var tasks = new List<Task>();

        foreach (var ip in range.Addresses())
        {
            token.ThrowIfCancellationRequested();
            if (exclude.Contains(IpRange.ToUInt(ip)))
            {
                onProgress(Interlocked.Increment(ref done));
                continue;
            }

            await gate.WaitAsync(token).ConfigureAwait(false);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var row = await PingOneAsync(ip, arpFallback, token).ConfigureAwait(false);
                    if (row is not null)
                    {
                        onFound(row);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 중지 요청 — 조용히 끝냄
                }
                finally
                {
                    onProgress(Interlocked.Increment(ref done));
                    gate.Release();
                }
            }, token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ScanRow?> PingOneAsync(IPAddress ip, IReadOnlyDictionary<string, string>? arpFallback, CancellationToken token)
    {
        using var ping = new Ping();
        PingReply reply;
        try
        {
            reply = await ping.SendPingAsync(ip, TimeoutMs, PingPayload).ConfigureAwait(false);
        }
        catch (PingException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (reply.Status != IPStatus.Success)
        {
            return null;
        }

        token.ThrowIfCancellationRequested();

        var mac = MacResolver.Resolve(ip, arpFallback);
        var vendor = OuiLookup.VendorFor(mac);
        var name = ResolveNames ? await TryResolveNameAsync(ip).ConfigureAwait(false) : string.Empty;
        var ttl = reply.Options?.Ttl;

        return new ScanRow(ip, reply.RoundtripTime, ttl, mac, vendor, name);
    }

    private static async Task<string> TryResolveNameAsync(IPAddress ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip).ConfigureAwait(false);
            var host = entry.HostName;
            return string.Equals(host, ip.ToString(), StringComparison.Ordinal) ? string.Empty : host;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>운영체제 ARP 캐시를 한 번 읽어 표로 만듭니다 (SendARP 가 실패할 때의 대비용).</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ReadArpCacheAsync(CancellationToken token)
    {
        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await proc.WaitForExitAsync(token).ConfigureAwait(false);
            return ArpTable.Parse(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new Dictionary<string, string>();
        }
    }
}
