using System.Net;
using System.Net.Sockets;
using System.Text;

public sealed class HueSsdpResponder(IPAddress localIp, int httpPort, HueBridgeStore store)
{
    private const string DiscoveryGroup = "239.255.255.250";
    private const int DiscoveryPort = 1900;

    private static bool IsHueSearch(string message)
    {
        if (!message.StartsWith("M-SEARCH * HTTP/1.1", StringComparison.OrdinalIgnoreCase) ||
            !message.Contains("ssdp:discover", StringComparison.OrdinalIgnoreCase))
            return false;
        return message.Contains("ST: ssdp:all", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ST: upnp:rootdevice", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ST: urn:schemas-upnp-org:device:basic:1", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ST: uuid:", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        udp.JoinMulticastGroup(IPAddress.Parse(DiscoveryGroup), localIp);
        Console.WriteLine($"[SSDP] Joined {DiscoveryGroup}:{DiscoveryPort} on {localIp}.");

        var uuid = "uuid:" + store.Uuid;
        var location = $"http://{localIp}:{httpPort}/description.xml";
        var prefix = "HTTP/1.1 200 OK\r\n" +
            "CACHE-CONTROL: max-age=100\r\n" +
            "EXT:\r\n" +
            "SERVER: Linux/3.14.0 UPnP/1.0 IpBridge/1.63.0\r\n" +
            $"LOCATION: {location}\r\n" +
            $"hue-bridgeid: {store.BridgeId}\r\n";

        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await udp.ReceiveAsync(cancellationToken);
            if (received.Buffer.Length > 4096) continue;
            string request = Encoding.ASCII.GetString(received.Buffer);
            if (!IsHueSearch(request)) continue;

            Console.WriteLine($"[SSDP] Hue-compatible M-SEARCH from {received.RemoteEndPoint.Address}");
            var variants = new (string SearchTarget, string Usn)[]
            {
                ("upnp:rootdevice", uuid + "::upnp:rootdevice"),
                (uuid, uuid),
                ("urn:schemas-upnp-org:device:basic:1", uuid)
            };
            foreach (var (st, usn) in variants)
            {
                var bytes = Encoding.ASCII.GetBytes(prefix + $"ST: {st}\r\nUSN: {usn}\r\n\r\n");
                await udp.SendAsync(bytes, bytes.Length, received.RemoteEndPoint);
            }
        }
    }
}
