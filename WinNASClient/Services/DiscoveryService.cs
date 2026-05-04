using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WinNASClient.Config;
using WinNASClient.Utils;

namespace WinNASClient.Services;

public class DiscoveryService
{
    private readonly AppConfig _config;
    private readonly int _discoveryPort = 19876;
    private UdpClient? _udpSender;
    private UdpClient? _udpReceiver;
    private CancellationTokenSource? _cts;
    private readonly Dictionary<string, PeerInfo> _peers = new();
    private readonly object _lock = new();

    public DiscoveryService(AppConfig config)
    {
        _config = config;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = BroadcastLoop(_cts.Token);
        _ = ListenLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udpSender?.Close();
        _udpReceiver?.Close();
    }

    public List<PeerInfo> GetPeers()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var expired = _peers.Where(p => (now - p.Value.LastSeen).TotalSeconds > 20).ToList();
            foreach (var p in expired)
                _peers.Remove(p.Key);

            return _peers.Values.ToList();
        }
    }

    private List<IPAddress> GetAllBroadcastAddresses()
    {
        var addresses = new List<IPAddress>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (ua.IPv4Mask == null || ua.IPv4Mask.Equals(IPAddress.Any)) continue;

                    var ipBytes = ua.Address.GetAddressBytes();
                    var maskBytes = ua.IPv4Mask.GetAddressBytes();
                    var broadcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                        broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

                    var broadcastAddr = new IPAddress(broadcastBytes);
                    if (!addresses.Contains(broadcastAddr))
                        addresses.Add(broadcastAddr);
                }
            }
        }
        catch { }

        if (addresses.Count == 0)
            addresses.Add(IPAddress.Broadcast);

        return addresses;
    }

    private List<string> GetAllLocalIPs()
    {
        var ips = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        ips.Add(ua.Address.ToString());
                }
            }
        }
        catch { }

        if (ips.Count == 0)
            ips.Add(NetworkHelper.GetLocalIpAddress());

        return ips;
    }

    private async Task BroadcastLoop(CancellationToken ct)
    {
        _udpSender = new UdpClient();
        _udpSender.EnableBroadcast = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var localIPs = GetAllLocalIPs();
                var machineName = Environment.MachineName;

                foreach (var localIP in localIPs)
                {
                    var msg = new DiscoveryMessage
                    {
                        Ip = localIP,
                        Port = _config.HttpPort,
                        MachineName = machineName,
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    };

                    var json = JsonSerializer.Serialize(msg);
                    var bytes = Encoding.UTF8.GetBytes(json);

                    var broadcastAddresses = GetAllBroadcastAddresses();
                    foreach (var broadcastAddr in broadcastAddresses)
                    {
                        try
                        {
                            var endpoint = new IPEndPoint(broadcastAddr, _discoveryPort);
                            await _udpSender.SendAsync(bytes, bytes.Length, endpoint);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            await Task.Delay(5000, ct);
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        _udpReceiver = new UdpClient(_discoveryPort);
        _udpReceiver.EnableBroadcast = true;

        var localIPs = GetAllLocalIPs();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpReceiver.ReceiveAsync().WithCancellation(ct);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var msg = JsonSerializer.Deserialize<DiscoveryMessage>(json);

                if (msg == null || string.IsNullOrEmpty(msg.Ip)) continue;

                if (localIPs.Contains(msg.Ip) && msg.Port == _config.HttpPort) continue;

                var key = $"{msg.Ip}:{msg.Port}";
                lock (_lock)
                {
                    _peers[key] = new PeerInfo
                    {
                        Ip = msg.Ip,
                        Port = msg.Port,
                        MachineName = msg.MachineName,
                        LastSeen = DateTime.UtcNow
                    };
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }
}

public class DiscoveryMessage
{
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public string MachineName { get; set; } = "";
    public long Timestamp { get; set; }
}

public class PeerInfo
{
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public string MachineName { get; set; } = "";
    public DateTime LastSeen { get; set; }
}

public static class TaskExtensions
{
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        using var registration = cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs);
        var completedTask = await Task.WhenAny(task, tcs.Task);
        if (completedTask == tcs.Task)
            throw new OperationCanceledException(cancellationToken);
        return await task;
    }
}
