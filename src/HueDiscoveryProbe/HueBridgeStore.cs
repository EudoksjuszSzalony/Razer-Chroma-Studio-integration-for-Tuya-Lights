using System.Security.Cryptography;
using System.Text.Json;

// Identity and paired local clients are stored under the Windows profile.
// Never write them to repository or print client tokens to console.
public sealed class HueBridgeStore
{
    private readonly object _gate = new();
    private readonly string _file;
    private HuePersisted _data;
    private HueLightState _light = new();

    public string Mac => _data.Mac;
    public string BridgeId => Mac[..6] + "FFFE" + Mac[6..];
    public string Serial => Mac.ToLowerInvariant();
    public string Uuid => $"2f402f80-da50-11e1-9b23-{Serial}";
    public string ColonMac => string.Join(":", Enumerable.Range(0, 6).Select(i => Mac.Substring(i * 2, 2))).ToLowerInvariant();

    public HueBridgeStore()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(baseDir, "ChromaTuyaBridge");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "hue-discovery.json");
        if (File.Exists(_file))
        {
            _data = JsonSerializer.Deserialize<HuePersisted>(File.ReadAllText(_file)) ??
                throw new InvalidDataException("Hue state file could not be read.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(_data.Mac ?? "", "^[A-F0-9]{12}$")
                || _data.Clients == null)
                throw new InvalidDataException("Invalid Hue state; do not delete it if clients are paired.");
        }
        else
        {
            var mac = RandomNumberGenerator.GetBytes(6);
            mac[0] = (byte)((mac[0] | 0x02) & 0xFE); // local-administered unicast MAC
            _data = new HuePersisted { Mac = Convert.ToHexString(mac) };
            Save();
        }
    }

    private void Save()
    {
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, _file, true);
    }

    public bool IsAuthorized(string user)
    {
        lock (_gate) return _data.Clients.ContainsKey(user);
    }

    public (string Username, string ClientKey) CreateUser()
    {
        lock (_gate)
        {
            if (_data.Clients.Count >= 12)
                throw new InvalidOperationException("Maximum number of test clients reached.");
            var username = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var clientKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _data.Clients[username] = clientKey;
            Save();
            return (username, clientKey);
        }
    }

    public object Light()
    {
        lock (_gate) return _light.Snapshot(ColonMac);
    }

    public object[] ApplyVirtualState(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return HueResources.Error(2, "/lights/1/state", "invalid state body");
        lock (_gate)
        {
            var changes = new List<object>();
            foreach (var property in element.EnumerateObject())
            {
                // Limit accepted Hue-v1 fields; never forward to Tuya.
                switch (property.Name)
                {
                    case "on" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        _light.On = property.Value.GetBoolean(); break;
                    case "bri" when property.Value.ValueKind == JsonValueKind.Number
                                  && property.Value.TryGetInt32(out var bri) && bri is >= 1 and <= 254:
                        _light.Bri = bri; break;
                    case "hue" when property.Value.ValueKind == JsonValueKind.Number
                                  && property.Value.TryGetInt32(out var hue) && hue is >= 0 and <= 65535:
                        _light.Hue = hue; _light.ColorMode = "hs"; break;
                    case "sat" when property.Value.ValueKind == JsonValueKind.Number
                                  && property.Value.TryGetInt32(out var sat) && sat is >= 0 and <= 254:
                        _light.Sat = sat; _light.ColorMode = "hs"; break;
                    case "ct" when property.Value.ValueKind == JsonValueKind.Number
                                  && property.Value.TryGetInt32(out var ct) && ct is >= 153 and <= 500:
                        _light.Ct = ct; _light.ColorMode = "ct"; break;
                    case "xy" when property.Value.ValueKind == JsonValueKind.Array
                                   && property.Value.GetArrayLength() == 2
                                   && property.Value[0].ValueKind == JsonValueKind.Number
                                   && property.Value[1].ValueKind == JsonValueKind.Number
                                   && property.Value[0].TryGetDouble(out var x)
                                   && property.Value[1].TryGetDouble(out var y)
                                   && x is >= 0 and <= 1 && y is >= 0 and <= 1:
                        _light.Xy = [x, y]; _light.ColorMode = "xy"; break;
                    default:
                        changes.Add(new { error = new { type = 7,
                            address = "/lights/1/state/" + property.Name,
                            description = "invalid or unsupported parameter" } });
                        continue;
                }
                changes.Add(new { success = new Dictionary<string, object> {
                    ["/lights/1/state/" + property.Name] = property.Value.Clone()
                } });
            }
            if (changes.Count != 0)
                Console.WriteLine("[Hue virtual lamp] In-memory state change received; no Tuya packets sent.");
            return changes.ToArray();
        }
    }
}

public sealed class HuePersisted
{
    public string Mac { get; set; } = "";
    public Dictionary<string, string> Clients { get; set; } = new();
}

public sealed class HueLightState
{
    public bool On { get; set; } = true;
    public int Bri { get; set; } = 102;
    public int Hue { get; set; } = 0;
    public int Sat { get; set; } = 254;
    public int Ct { get; set; } = 366;
    public double[] Xy { get; set; } = [0.675, 0.322];
    public string ColorMode { get; set; } = "hs";

    public object Snapshot(string mac) => new
    {
        state = new { on = On, bri = Bri, hue = Hue, sat = Sat, ct = Ct,
            xy = Xy, colormode = ColorMode, effect = "none",
            alert = "none", reachable = true },
        type = "Extended color light",
        name = "Tuya Chroma Virtual Lamp (TEST)",
        modelid = "LCT015",
        manufacturername = "Hue Bridge Emulator (community)",
        productname = "Virtual RGB lamp — no real hardware control",
        uniqueid = mac + "-01",
        swversion = "1.100.0",
        capabilities = new { certified = false, control = new {
            mindimlevel = 1000, maxlumen = 800, colorgamuttype = "C",
            ct = new { min = 153, max = 500 }
        }, streaming = new { renderer = true, proxy = false } }
    };
}
