using System.Net;
using System.Text.Json;

if (args.Contains("--pairing-selftest", StringComparer.OrdinalIgnoreCase))
{
    bool passed = HuePairingWindow.RunSelfTest();
    Console.WriteLine(passed ? "Hue push-link window self-test: OK" : "Hue push-link window self-test: FAILED");
    return passed ? 0 : 1;
}

if (args.Contains("--mdns-selftest", StringComparer.OrdinalIgnoreCase))
{
    bool passed = HueMdnsResponder.RunOfflineSelfTest();
    Console.WriteLine(passed ? "Hue mDNS offline packet self-test: OK" : "Hue mDNS offline packet self-test: FAILED");
    return passed ? 0 : 1;
}

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        Native Windows Hue Bridge discovery prototype (NO Tuya hardware writes).
        Usage: dotnet run --project src/HueDiscoveryProbe -- --ip <local-LAN-IPv4> [options]
        Options:
          --port <1..65535>          HTTP port (default 80; Hue clients often require 80)
          --no-ssdp                  Disable SSDP UDP/1900 (for HTTP tests)
          --no-mdns                  Disable Hue mDNS UDP/5353 (for HTTP tests)
          --mdns-selftest            Validate mDNS query/response packets offline
          --pairing-selftest         Validate push-link 0/120-second window offline
          --pairing-seconds <0..300> Open simulated pairing window on startup (default 0)
          --help                     Show this help

        Binds the HTTP server only to the specified local IP, not all interfaces.
        Simulates ONE lamp in memory; never contacts Tuya or changes real lights.
        Requires local Windows firewall permission for client/device discovery.
        Does NOT implement Hue Entertainment/DTLS or HTTPS API v2.
        """);
    return 0;
}

string? ipText = null;
int port = 80, pairingSeconds = 0;
bool ssdpEnabled = true, mdnsEnabled = true;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ip" when i + 1 < args.Length:
            ipText = args[++i]; break;
        case "--port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var requestedPort)
            && requestedPort >= 1 && requestedPort <= 65535:
            port = requestedPort; i++; break;
        case "--pairing-seconds" when i + 1 < args.Length
            && int.TryParse(args[i + 1], out var requestedSeconds)
            && requestedSeconds is >= 0 and <= 300:
            pairingSeconds = requestedSeconds; i++; break;
        case "--no-ssdp":
            ssdpEnabled = false; break;
        case "--no-mdns":
            mdnsEnabled = false; break;
        default:
            Console.Error.WriteLine($"Invalid argument: {args[i]}. Use --help.");
            return 2;
    }
}
if (!IPAddress.TryParse(ipText, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
    || ip.Equals(IPAddress.Any) || ip.IsIPv6Multicast || (ip.GetAddressBytes()[0] >= 224))
{
    Console.Error.WriteLine("Pass the IPv4 address of your Windows PC using --ip (not the Tuya gateway IP).");
    return 2;
}

var store = new HueBridgeStore();
var localUrl = $"http://{ip}:{port}";
var pairingWindow = new HuePairingWindow(pairingSeconds);
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls(localUrl);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
var app = builder.Build();

app.MapGet("/", () => Results.Content(
    "Chroma Tuya Hue Discovery Prototype — virtual light only; no Tuya writes. " +
    "Try /description.xml or /api/nouser/config", "text/plain"));

app.MapGet("/description.xml", () => Results.Content(
    HueDescription.Render(ip, port, store), "text/xml; charset=utf-8"));
app.MapGet("/api/config", () => Results.Json(HueResources.Config(ip, store, pairingWindow.IsPressed)));
app.MapGet("/api/nouser/config", () =>
    Results.Json(HueResources.Config(ip, store, pairingWindow.IsPressed)));

// Hue v1 bridge user creation. Pairing is opt-in and temporary.
app.MapPost("/api", async (HttpContext ctx) =>
{
    if (!pairingWindow.IsPressed)
    {
        Console.WriteLine("[Hue pairing] Registration attempt rejected: push-link button is not active.");
        return Results.Json(HueResources.Error(101, "", "link button not pressed"));
    }

    try
    {
        using var document = await JsonDocument.ParseAsync(ctx.Request.Body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("devicetype", out var deviceType) ||
            deviceType.ValueKind != JsonValueKind.String ||
            deviceType.GetString() is not { Length: > 0 and <= 128 })
            return Results.Json(HueResources.Error(2, "", "body contains invalid devicetype"));

        var includeKey = root.TryGetProperty("generateclientkey", out var wantsKey)
                         && wantsKey.ValueKind == JsonValueKind.True;
        var (user, key) = store.CreateUser();
        Console.WriteLine("[Hue pairing] Virtual client registered (username withheld from logs).");
        var success = includeKey
            ? new { success = (object)new { username = user, clientkey = key } }
            : new { success = (object)new { username = user } };
        return Results.Json(new[] { success });
    }
    catch (JsonException)
    {
        return Results.Json(HueResources.Error(2, "", "invalid JSON"));
    }
});

app.MapGet("/api/{user}", (string user) =>
    store.IsAuthorized(user)
        ? Results.Json(HueResources.Overview(ip, store, pairingWindow.IsPressed))
        : Results.Json(HueResources.Error(1, "/", "unauthorized user")));

app.MapGet("/api/{user}/lights", (string user) =>
    store.IsAuthorized(user)
        ? Results.Json(HueResources.Lights(store))
        : Results.Json(HueResources.Error(1, "/lights", "unauthorized user")));

app.MapGet("/api/{user}/lights/{light}", (string user, string light) =>
    !store.IsAuthorized(user)
        ? Results.Json(HueResources.Error(1, "/lights", "unauthorized user"))
        : light == "1"
            ? Results.Json(store.Light())
            : Results.Json(HueResources.Error(3, "/lights", "resource not available")));

app.MapGet("/api/{user}/groups", (string user) =>
    store.IsAuthorized(user)
        ? Results.Json(HueResources.Groups())
        : Results.Json(HueResources.Error(1, "/groups", "unauthorized user")));

app.MapGet("/api/{user}/groups/{group}", (string user, string group) =>
    !store.IsAuthorized(user)
        ? Results.Json(HueResources.Error(1, "/groups", "unauthorized user"))
        : HueResources.Groups().TryGetValue(group, out var data)
            ? Results.Json(data)
            : Results.Json(HueResources.Error(3, "/groups", "resource not available")));

app.MapGet("/api/{user}/config", (string user) =>
    store.IsAuthorized(user)
        ? Results.Json(HueResources.Config(ip, store, pairingWindow.IsPressed))
        : Results.Json(HueResources.Error(1, "/config", "unauthorized user")));

foreach (var path in new[] { "scenes", "schedules", "rules", "resourcelinks", "sensors" })
{
    app.MapGet($"/api/{{user}}/{path}", (string user) =>
        store.IsAuthorized(user)
            ? Results.Json(new Dictionary<string, object>())
            : Results.Json(HueResources.Error(1, "/", "unauthorized user")));
}

app.MapGet("/api/{user}/capabilities", (string user) =>
    store.IsAuthorized(user)
        ? Results.Json(new { lights = new { available = 1 }, groups = new { available = 1 },
            sensors = new { available = 0 }, scenes = new { available = 0 } })
        : Results.Json(HueResources.Error(1, "/", "unauthorized user")));

// Reflect ordinary Hue-v1 static state requests IN MEMORY ONLY: no Smart Life or Tuya call.
app.MapPut("/api/{user}/lights/1/state", async (HttpContext context, string user) =>
{
    if (!store.IsAuthorized(user))
        return Results.Json(HueResources.Error(1, "/lights/1/state", "unauthorized user"));
    try
    {
        using var doc = await JsonDocument.ParseAsync(context.Request.Body);
        return Results.Json(store.ApplyVirtualState(doc.RootElement));
    }
    catch (JsonException)
    {
        return Results.Json(HueResources.Error(2, "/lights/1/state", "invalid JSON"));
    }
});

// Don't silently claim that the Entertainment DTLS stream started.
app.MapPut("/api/{user}/groups/1", (string user) =>
    Results.Json(HueResources.Error(store.IsAuthorized(user) ? 901 : 1,
        "/groups/1", "changing group is not supported by this discovery-only prototype")));
app.MapPut("/api/{user}/groups/1/stream", (string user) =>
{
    Console.WriteLine("[Hue stream] Client requested Entertainment streaming; DTLS not implemented.");
    return Results.Json(HueResources.Error(store.IsAuthorized(user) ? 901 : 1,
        "/groups/1/stream", "Hue Entertainment DTLS is not implemented in this prototype"));
});

using var ssdpCts = new CancellationTokenSource();
Task? ssdpTask = null;
if (ssdpEnabled)
{
    ssdpTask = Task.Run(async () =>
    {
        try
        {
            await new HueSsdpResponder(ip, port, store).RunAsync(ssdpCts.Token);
        }
        catch (OperationCanceledException) when (ssdpCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[SSDP] " + ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine("[SSDP] Discovery may be unavailable; HTTP remains active.");
        }
    });
}
else Console.WriteLine("SSDP multicast disabled (--no-ssdp).");

Task? mdnsTask = null;
if (mdnsEnabled)
{
    mdnsTask = Task.Run(async () =>
    {
        try
        {
            await new HueMdnsResponder(ip, port, store).RunAsync(ssdpCts.Token);
        }
        catch (OperationCanceledException) when (ssdpCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[mDNS] " + ex.GetType().Name + ": " + ex.Message);
            Console.Error.WriteLine("[mDNS] Hue mDNS discovery unavailable; HTTP and SSDP remain independent.");
        }
    });
}
else Console.WriteLine("Hue mDNS disabled (--no-mdns).");

Console.WriteLine("=== Chroma Tuya virtual Hue bridge: DISCOVERY PROTOTYPE ===");
Console.WriteLine($"Bridge URL: {localUrl}");
Console.WriteLine($"Description: {localUrl}/description.xml");
Console.WriteLine("Virtual lamp: 1 (memory only, no physical lamp changes).");
Console.WriteLine(pairingSeconds == 0 ? "Pairing disabled. Restart with --pairing-seconds 120 to allow pairing." :
    $"Pairing enabled for {pairingSeconds} seconds after startup.");
Console.WriteLine("Hue Entertainment DTLS and HTTPS/v2 NOT IMPLEMENTED.");
Console.WriteLine("Press Ctrl+C to stop.");

try { await app.RunAsync(); }
finally
{
    ssdpCts.Cancel();
    if (ssdpTask != null) try { await ssdpTask; } catch (OperationCanceledException) { }
    if (mdnsTask != null) try { await mdnsTask; } catch (OperationCanceledException) { }
}
return 0;
