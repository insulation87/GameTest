using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

public static class LanAccess
{
    public static bool IsLocal(IPAddress target)
    {
        if (target == null || target.AddressFamily != AddressFamily.InterNetwork) return false;
        if (IPAddress.IsLoopback(target)) return true;
        byte[] remote = target.GetAddressBytes();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (entry.Address.AddressFamily != AddressFamily.InterNetwork || entry.IPv4Mask == null) continue;
                    byte[] local = entry.Address.GetAddressBytes(), mask = entry.IPv4Mask.GetAddressBytes();
                    bool same = true, any = false;
                    for (int i = 0; i < 4; i++) { any |= mask[i] != 0; same &= (remote[i] & mask[i]) == (local[i] & mask[i]); }
                    if (same && any) return true;
                }
            }
        }
        catch (NetworkInformationException) { }
        return false;
    }
}
