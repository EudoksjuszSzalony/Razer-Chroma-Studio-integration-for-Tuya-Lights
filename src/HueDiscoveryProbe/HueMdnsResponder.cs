using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Minimal DNS-SD/mDNS responder for one virtual Hue service.
// Advertises only synthetic device information; never touches physical lamps.
// Reference: diyHue BridgeEmulator/services/mdns.py (service type/metadata).
public sealed class HueMdnsResponder(IPAddress localIp, int httpPort, HueBridgeStore store)
{
    private static readonly IPAddress Multicast = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;
    private const string ServiceType = "_hue._tcp.local";
    private string Instance => $"ChromaTuya-{store.BridgeId[^6..]}.{ServiceType}";
    private string Server => $"ChromaTuya-{store.BridgeId}.local";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
        udp.JoinMulticastGroup(Multicast, localIp);
        udp.Client.SetSocketOption(SocketOptionLevel.IP,
            SocketOptionName.MulticastInterface, localIp.GetAddressBytes());
        udp.Client.SetSocketOption(SocketOptionLevel.IP,
            SocketOptionName.MulticastTimeToLive, 255);
        Console.WriteLine($"[mDNS] Listening on {Multicast}:{MdnsPort} ({localIp}), advertising {ServiceType}.");

        var response = CreateResponse(localIp, httpPort, store.BridgeId, Instance, Server);
        // Announce only the virtual Hue service on the selected local LAN.
        await udp.SendAsync(response, response.Length, new IPEndPoint(Multicast, MdnsPort));
        Console.WriteLine($"[mDNS] Initial Hue service announcement sent ({localIp}:{httpPort}).");

        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await udp.ReceiveAsync(cancellationToken);
            if (received.Buffer.Length is < 12 or > 4096) continue;
            if (!ContainsHueQuery(received.Buffer, Instance, Server)) continue;

            Console.WriteLine($"[mDNS] Hue discovery query from {received.RemoteEndPoint.Address}; replying with PTR/SRV/TXT/A.");
            await udp.SendAsync(response, response.Length, new IPEndPoint(Multicast, MdnsPort));
        }
    }

    internal static bool ContainsHueQuery(ReadOnlySpan<byte> packet, string instance, string server)
    {
        if (packet.Length < 12) return false;
        // Only DNS standard queries; never respond to another responder's answers.
        if ((packet[2] & 0x80) != 0) return false;
        int count = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4, 2));
        if (count is < 1 or > 32) return false;

        int position = 12;
        for (int n = 0; n < count; n++)
        {
            if (!TryReadName(packet, ref position, out var name) || position + 4 > packet.Length)
                return false;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(position, 2));
            ushort qclass = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(position + 2, 2));
            position += 4;

            if ((qclass & 0x7fff) != 1) continue;
            if (name.Equals(ServiceType, StringComparison.OrdinalIgnoreCase) && (type is 12 or 255))
                return true;
            if (name.Equals(instance, StringComparison.OrdinalIgnoreCase) && (type is 16 or 33 or 255))
                return true;
            if (name.Equals(server, StringComparison.OrdinalIgnoreCase) && (type is 1 or 255))
                return true;
        }
        return false;
    }

    private static bool TryReadName(ReadOnlySpan<byte> packet, ref int position, out string name)
    {
        var labels = new List<string>();
        int cursor = position;
        bool jumped = false;
        for (int n = 0; n < 80; n++)
        {
            if (cursor >= packet.Length) break;
            int len = packet[cursor];
            if (len == 0)
            {
                if (!jumped) position = cursor + 1;
                name = string.Join(".", labels);
                return true;
            }
            if ((len & 0xc0) == 0xc0)
            {
                if (cursor + 1 >= packet.Length) break;
                int pointer = ((len & 0x3f) << 8) | packet[cursor + 1];
                if (pointer >= packet.Length) break;
                if (!jumped) position = cursor + 2;
                jumped = true;
                cursor = pointer;
                continue;
            }
            if (len > 63 || cursor + 1 + len > packet.Length) break;
            labels.Add(Encoding.ASCII.GetString(packet.Slice(cursor + 1, len)));
            cursor += len + 1;
        }
        name = "";
        return false;
    }

    internal static byte[] CreateResponse(IPAddress ip, int port,
        string bridgeId, string instance, string host)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("mDNS response requires IPv4.", nameof(ip));

        var output = new List<byte>(512);
        // Standard mDNS authoritative response; one PTR answer, three additions.
        U16(output, 0); U16(output, 0x8400);
        U16(output, 0); U16(output, 1); U16(output, 0); U16(output, 3);

        Record(output, ServiceType, 12, 1, DnsName(instance));
        var srv = new List<byte>();
        U16(srv, 0); U16(srv, 0); U16(srv, port);
        srv.AddRange(DnsName(host));
        Record(output, instance, 33, 0x8001, srv.ToArray());

        var txt = new List<byte>();
        foreach (var entry in new[] { "modelid=BSB002", "bridgeid=" + bridgeId })
        {
            var field = Encoding.ASCII.GetBytes(entry);
            txt.Add((byte)field.Length);
            txt.AddRange(field);
        }
        Record(output, instance, 16, 0x8001, txt.ToArray());
        Record(output, host, 1, 0x8001, ip.GetAddressBytes());
        return output.ToArray();
    }

    private static void Record(List<byte> output, string name, int type, int rrClass, byte[] value)
    {
        output.AddRange(DnsName(name));
        U16(output, type);
        U16(output, rrClass);
        U32(output, 120); // TTL seconds
        U16(output, value.Length);
        output.AddRange(value);
    }

    private static byte[] DnsName(string name)
    {
        var result = new List<byte>();
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Invalid DNS label.");
            result.Add((byte)bytes.Length);
            result.AddRange(bytes);
        }
        result.Add(0);
        return result.ToArray();
    }

    private static void U16(List<byte> b, int value)
    {
        b.Add((byte)(value >> 8)); b.Add((byte)value);
    }

    private static void U32(List<byte> b, int value)
    {
        b.Add((byte)(value >> 24)); b.Add((byte)(value >> 16));
        b.Add((byte)(value >> 8)); b.Add((byte)value);
    }

    public static bool RunOfflineSelfTest()
    {
        const string id = "AABBCCFFFEDDEEFF";
        string instance = "ChromaTuya-DDEEFF._hue._tcp.local";
        string host = "ChromaTuya-AABBCCFFFEDDEEFF.local";
        var query = new List<byte>(new byte[12]);
        query[5] = 1;
        query.AddRange(DnsName(ServiceType));
        U16(query, 12); U16(query, 1);
        var negative = new List<byte>(new byte[12]);
        negative[5] = 1;
        negative.AddRange(DnsName("_printer._tcp.local"));
        U16(negative, 12); U16(negative, 1);
        var response = CreateResponse(IPAddress.Parse("192.168.1.10"), 80, id, instance, host);
        return ContainsHueQuery(query.ToArray(), instance, host)
            && !ContainsHueQuery(negative.ToArray(), instance, host)
            && !ContainsHueQuery(response, instance, host)
            && BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(6, 2)) == 1
            && BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(10, 2)) == 3
            && Encoding.ASCII.GetString(response).Contains("bridgeid=" + id);
    }
}
