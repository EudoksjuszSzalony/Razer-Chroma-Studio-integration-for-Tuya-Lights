// MIT-licensed experimental receiver using ChromaControl.SDK.Synapse.
// Reads incoming Connect colors; never writes Razer or Tuya lighting.
using System.Text.Json;
using ChromaControl.SDK.Synapse;
using ChromaControl.SDK.Synapse.Extensions;
using ChromaControl.SDK.Synapse.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Chroma Connect receiver requires Windows and Synapse.");
    return 2;
}

var appId = Environment.GetEnvironmentVariable("CHROMACONTROL_KEY_SYNAPSE");
if (!Guid.TryParse(appId, out _))
{
    Console.Error.WriteLine("Set CHROMACONTROL_KEY_SYNAPSE to an authorized Chroma Connect GUID.");
    Console.Error.WriteLine("Do not use IDs belonging to other Razer partners.");
    return 2;
}

// The native Razer Chroma Connect API is distributed with compatible Razer software.
// The managed NuGet SDK alone cannot emulate a registered/authorized partner device.
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSynapseSDK();
    builder.Services.AddHostedService<ProbeWorker>();
    await builder.Build().RunAsync();
    return 0;
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("RzChromaConnectAPI64.dll was not found. Check the installed Chroma Connect components.");
    return 3;
}
catch (BadImageFormatException)
{
    Console.Error.WriteLine("The Chroma Connect native DLL architecture does not match this build.");
    return 3;
}

public sealed class ProbeWorker : BackgroundService
{
    private readonly ISynapseService _synapse;

    public ProbeWorker(ISynapseService synapse)
    {
        _synapse = synapse;
        _synapse.ColorsReceived += OnColors;
        _synapse.StatusChanged += OnStatus;
    }

    private static void OnStatus(object? sender, SynapseStatus status)
    {
        Console.Error.WriteLine("Synapse Connect status: " + status);
    }

    private static void OnColors(object? sender, System.Drawing.Color[] colors)
    {
        if (colors.Length != 5) return;
        var rgb = colors.Select(c => new { r = (int)c.R, g = (int)c.G, b = (int)c.B }).ToArray();
        Console.Out.WriteLine(JsonSerializer.Serialize(new { type = "colors", rgb }));
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.Error.WriteLine("Waiting for Chroma Connect RGB events; this is a read-only probe.");
        return Task.CompletedTask;
    }
}
