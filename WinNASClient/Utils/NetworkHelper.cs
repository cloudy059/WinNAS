using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WinNASClient.Utils;

public static class NetworkHelper
{
    public static List<NetworkInterfaceInfo> GetNetworkInterfaces()
    {
        var result = new List<NetworkInterfaceInfo>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var ipProps = ni.GetIPProperties();
            foreach (var ip in ipProps.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add(new NetworkInterfaceInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        IPAddress = ip.Address.ToString(),
                        SubnetMask = ip.IPv4Mask?.ToString() ?? "",
                        Gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "",
                        IsUp = ni.OperationalStatus == OperationalStatus.Up,
                        Speed = ni.Speed
                    });
                }
            }
        }

        return result;
    }

    public static string GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }

    public static bool IsPortInUse(int port)
    {
        var ipGlobalProps = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfo = ipGlobalProps.GetActiveTcpListeners();
        return tcpConnInfo.Any(x => x.Port == port);
    }
}

public class NetworkInterfaceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public bool IsUp { get; set; }
    public long Speed { get; set; }
}
