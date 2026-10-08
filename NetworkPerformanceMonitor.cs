using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace HELLDRIVE;

public sealed record NetworkSample(
    DateTime Timestamp,
    string Host,
    long? PingMs,
    bool Success);

public sealed class NetworkPerformanceMonitor
{
    public async Task<NetworkSample> SampleAsync(string host = "1.1.1.1")
    {
        using var ping = new Ping();

        try
        {
            var reply = await ping.SendPingAsync(host, 2000);
            return new NetworkSample(
                DateTime.Now,
                host,
                reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
                reply.Status == IPStatus.Success);
        }
        catch
        {
            return new NetworkSample(DateTime.Now, host, null, false);
        }
    }
}