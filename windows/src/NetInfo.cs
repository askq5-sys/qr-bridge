// Адреса компьютера в локальной сети: домашняя сеть первой, VPN и виртуальные в конце.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace QrBridge
{
    public static class NetInfo
    {
        static readonly string[] VirtualMarks = {
            "virtual", "vmware", "hyper-v", "vethernet", "vpn", "wintun",
            "wireguard", "tap-windows", "tun", "zerotier", "tailscale"
        };

        static int PrefixScore(string ip)
        {
            if (ip.StartsWith("192.168.")) return 0;
            if (ip.StartsWith("10.")) return 2;
            if (ip.StartsWith("198.18.") || ip.StartsWith("198.19.") || ip.StartsWith("100.")) return 10;
            if (ip.StartsWith("172.")) return 8;
            return 6;
        }

        public static List<string> LocalIps()
        {
            Dictionary<string, int> scores = new Dictionary<string, int>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();

                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any))
                            hasGateway = true;
                    bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    bool isVirtual = false;
                    foreach (string mark in VirtualMarks)
                        if (desc.Contains(mark)) isVirtual = true;

                    foreach (UnicastIPAddressInformation a in props.UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = a.Address.ToString();
                        if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                        int score = PrefixScore(ip) + (hasGateway ? 0 : 20) + (isVirtual ? 40 : 0) + (wifi ? 0 : 1);
                        int old;
                        if (!scores.TryGetValue(ip, out old) || old > score) scores[ip] = score;
                    }
                }
            }
            catch (Exception) { }

            List<string> list = new List<string>(scores.Keys);
            list.Sort(delegate (string x, string y) { return scores[x].CompareTo(scores[y]); });
            if (list.Count == 0) list.Add("127.0.0.1");
            return list;
        }
    }
}
