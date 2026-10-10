using System.Net;
using System.Xml.Linq;

public static class HueDescription
{
    public static string Render(IPAddress ip, int port, HueBridgeStore store)
    {
        XNamespace ns = "urn:schemas-upnp-org:device-1-0";
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "root",
                new XElement(ns + "specVersion",
                    new XElement(ns + "major", "1"), new XElement(ns + "minor", "0")),
                new XElement(ns + "URLBase", $"http://{ip}:{port}/"),
                new XElement(ns + "device",
                    new XElement(ns + "deviceType", "urn:schemas-upnp-org:device:Basic:1"),
                    new XElement(ns + "friendlyName", $"Chroma Tuya Virtual Hue Bridge ({ip})"),
                    new XElement(ns + "manufacturer", "Signify"),
                    new XElement(ns + "manufacturerURL", "https://www.philips-hue.com"),
                    new XElement(ns + "modelDescription", "Philips hue Personal Wireless Lighting"),
                    new XElement(ns + "modelName", "Philips hue bridge 2015"),
                    new XElement(ns + "modelNumber", "BSB002"),
                    new XElement(ns + "serialNumber", store.Serial),
                    new XElement(ns + "UDN", "uuid:" + store.Uuid),
                    new XElement(ns + "presentationURL", "index.html"))));
        return document.ToString();
    }
}

public static class HueResources
{
    public static object[] Error(int type, string address, string description) =>
        [new { error = new { type, address, description } }];

    public static object Config(IPAddress ip, HueBridgeStore store) => new
    {
        name = "Chroma Tuya Hue Bridge (TEST)",
        zigbeechannel = 11,
        mac = store.ColonMac,
        dhcp = true,
        ipaddress = ip.ToString(),
        netmask = "255.255.255.0",
        modelid = "BSB002",
        bridgeid = store.BridgeId,
        factorynew = false,
        replacesbridgeid = (string?)null,
        starterkitid = "",
        swversion = "1959097030",
        apiversion = "1.52.0",
        datastoreversion = "93",
        linkbutton = false,
        portalservices = false,
        portalconnection = "disconnected",
        portalstate = new { signedon = false, incoming = false, outgoing = false,
            communication = "disconnected" },
        internetservices = new { internet = "disconnected", remoteaccess = "disconnected",
            time = "disconnected", swupdate = "disconnected" },
        whitelist = new Dictionary<string, object>(),
        swupdate = new { updatestate = 0, checkforupdate = false,
            devicetypes = new { bridge = false, lights = Array.Empty<string>(), sensors = Array.Empty<string>() },
            url = "", text = "", notify = false },
        timezone = "Europe/Warsaw",
        UTC = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
        localtime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
    };

    public static Dictionary<string, object> Lights(HueBridgeStore store) =>
        new() { ["1"] = store.Light() };

    public static Dictionary<string, object> Groups() => new()
    {
        ["0"] = new {
            name = "All lights", type = "LightGroup", lights = new[] { "1" },
            sensors = Array.Empty<string>(), state = new { all_on = true, any_on = true },
            action = GroupAction()
        },
        ["1"] = new {
            name = "Chroma Tuya Entertainment (TEST)", type = "Entertainment",
            lights = new[] { "1" }, sensors = Array.Empty<string>(),
            state = new { all_on = true, any_on = true },
            action = GroupAction(),
            locations = new Dictionary<string, double[]> { ["1"] = [0, 0, 0] },
            recycle = false,
            @class = "TV",
            stream = new { proxymode = "auto", proxynode = "/bridge",
                active = false, owner = (string?)null }
        }
    };

    private static object GroupAction() => new
    {
        on = true, bri = 102, hue = 0, sat = 254, effect = "none",
        xy = new double[] { 0.675, 0.322 }, ct = 366, alert = "none", colormode = "hs"
    };

    public static object Overview(IPAddress ip, HueBridgeStore store) => new
    {
        lights = Lights(store), groups = Groups(),
        config = Config(ip, store),
        scenes = new Dictionary<string, object>(),
        schedules = new Dictionary<string, object>(),
        sensors = new Dictionary<string, object>(),
        rules = new Dictionary<string, object>(),
        resourcelinks = new Dictionary<string, object>()
    };
}
