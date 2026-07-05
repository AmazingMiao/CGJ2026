#nullable enable

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CGJ2026.Gyro
{
    /// 取本机局域网 IPv4 的工具。手机热点/路由器下用于拼二维码 URL、传给 TLS 代理绑定证书。
    public static class LanAddress
    {
        /// 返回本机主用局域网 IPv4;取不到时回落到 127.0.0.1。
        /// 优先私有网段(手机热点 172.20.10.x、192.168.x、10.x),跳过回环与虚拟网卡。
        public static string GetLanIPv4()
        {
            string? fallback = null;

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation info in ni.GetIPProperties().UnicastAddresses)
                {
                    IPAddress addr = info.Address;
                    if (addr.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr))
                    {
                        continue;
                    }

                    string text = addr.ToString();
                    if (IsPrivate(text))
                    {
                        return text;
                    }

                    fallback ??= text;
                }
            }

            return fallback ?? "127.0.0.1";
        }

        static bool IsPrivate(string ip)
        {
            return ip.StartsWith("192.168.") ||
                   ip.StartsWith("10.") ||
                   ip.StartsWith("172.20.10.") ||
                   IsClassBPrivate(ip);
        }

        // 172.16.0.0 ~ 172.31.255.255。
        static bool IsClassBPrivate(string ip)
        {
            if (!ip.StartsWith("172."))
            {
                return false;
            }

            int dot = ip.IndexOf('.', 4);
            if (dot < 0)
            {
                return false;
            }

            return int.TryParse(ip.Substring(4, dot - 4), out int second) && second >= 16 && second <= 31;
        }
    }
}
