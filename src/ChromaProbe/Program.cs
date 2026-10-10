// Read-only receiver for Chroma Connect RGB events (ChromaControl.SDK.Synapse, MIT).
// --diagnose only loads a native DLL and checks exported functions; it NEVER calls Init.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ChromaControl.SDK.Synapse;
using ChromaControl.SDK.Synapse.Extensions;
using ChromaControl.SDK.Synapse.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This Chroma Connect receiver requires Windows.");
    return 2;
}

var dllPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    "Razer", "ChromaBroadcast", "bin", "RzChromaConnectAPI64.dll");

if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("=== Native Chroma Connect diagnostics (read-only) ===");
    Console.WriteLine($"OS: {Environment.OSVersion}");
    Console.WriteLine($".NET runtime: {Environment.Version}");
    Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
    Console.WriteLine($"DLL path: {dllPath}");
    if (!File.Exists(dllPath))
    {
        Console.WriteLine("DLL status: NOT FOUND at expected location.");
        Console.WriteLine("No Synapse registration was attempted.");
        return 0;
    }
    var info = FileVersionInfo.GetVersionInfo(dllPath);
    Console.WriteLine($"File version: {info.FileVersion ?? "unknown"}");
    Console.WriteLine("Checking Windows DLL loader search paths (no Connect Init called):");

    // See Microsoft LoadLibraryEx docs: DLL_LOAD_DIR alone excludes the
    // normal DLL search path. Adding DEFAULT_DIRS includes System32.
    // This was the likely reason the original --diagnose reported
    // DllNotFoundException even though the Razer DLL was present.
    var tests = new (string Label, uint Flags)[]
    {
        ("Razer DLL directory only", Win32DllLoader.LoadLibrarySearchDllLoadDir),
        ("Razer DLL directory + safe system/application dirs",
            Win32DllLoader.LoadLibrarySearchDllLoadDir |
            Win32DllLoader.LoadLibrarySearchDefaultDirs)
    };

    foreach (var (label, flags) in tests)
    {
        var (native, errorCode) = Win32DllLoader.TryLoad(dllPath, flags);
        if (native == IntPtr.Zero)
        {
            var message = new System.ComponentModel.Win32Exception(errorCode).Message;
            Console.WriteLine($"  {label}: FAILED, Win32 {errorCode} ({message})");
            continue;
        }

        Console.WriteLine($"  {label}: LOADED");
        try
        {
            foreach (var entry in new[]
            {
                "Init", "UnInit", "RegisterEventNotification", "UnRegisterEventNotification"
            })
            {
                Console.WriteLine($"    {entry}: " +
                    (NativeLibrary.TryGetExport(native, entry, out _) ? "AVAILABLE" : "NOT FOUND"));
            }
        }
        finally
        {
            Win32DllLoader.Unload(native);
        }
    }
    Console.WriteLine("Diagnostics finished: no Chroma Connect Init or device registration attempted.");
    return 0;
}

if (args.Any(arg => arg.StartsWith("-", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Usage: dotnet run --project src/ChromaProbe -- --diagnose");
    Console.Error.WriteLine("   or: dotnet run --project src/ChromaProbe  (authorized Chroma Connect GUID required)");
    return 2;
}

var appIdString = Environment.GetEnvironmentVariable("CHROMACONTROL_KEY_SYNAPSE");
if (!Guid.TryParse(appIdString, out var appId) || appId == Guid.Empty)
{
    Console.Error.WriteLine("No authorized Chroma Connect partner GUID supplied.");
    Console.Error.WriteLine("Use --diagnose first. An arbitrary/generated GUID does not grant device access.");
    Console.Error.WriteLine("Never use another manufacturer's registered GUID.");
    return 2;
}

if (!File.Exists(dllPath))
{
    Console.Error.WriteLine("Native RzChromaConnectAPI64.dll not found at the expected Razer install path.");
    return 3;
}

// Third-party SDK's P/Invoke is in its own assembly. Resolve its native
// imports directly to the verified local Razer DLL, without copying binaries,
// modifying system PATH, or replacing any Razer file.
try
{
    NativeLibrary.SetDllImportResolver(
        typeof(SynapseService).Assembly,
        (libraryName, assembly, searchPath) =>
        {
            if (libraryName == "RzChromaConnectAPI64")
                return NativeLibrary.Load(
                    dllPath, assembly, DllImportSearchPath.UseDllDirectoryForDependencies |
                        DllImportSearchPath.SafeDirectories);
            return IntPtr.Zero;
        });

    Console.Error.WriteLine("Starting read-only Chroma Connect receiver...");
    Console.Error.WriteLine("If no events arrive, check partner GUID authorization in Synapse.");
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddSynapseSDK();
    builder.Services.AddHostedService<ProbeWorker>();
    await builder.Build().RunAsync();
    return 0;
}
catch (DllNotFoundException)
{
    Console.Error.WriteLine("Failed to load native Chroma Connect DLL or one of its dependencies.");
    return 3;
}
catch (BadImageFormatException)
{
    Console.Error.WriteLine("Chroma Connect native DLL architecture does not match the process.");
    return 3;
}
catch (InvalidOperationException)
{
    Console.Error.WriteLine("Native import resolver conflict or invalid Chroma initialization.");
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
        Console.Error.WriteLine("Waiting for authorized Chroma Connect RGB events (Ctrl+C to stop).");
        return Task.CompletedTask;
    }
}

internal static class Win32DllLoader
{
    // Fixed search paths: DLL directory, application directory, and System32.
    // Never alter the process PATH, copy native DLLs, or load dependencies
    // from the current working directory.
    internal const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    internal const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(string path, nint reserved, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint libraryHandle);

    internal static (nint Handle, int ErrorCode) TryLoad(string path, uint flags)
    {
        var handle = LoadLibraryExW(path, IntPtr.Zero, flags);
        return (handle, handle == IntPtr.Zero ? Marshal.GetLastPInvokeError() : 0);
    }

    internal static void Unload(nint handle) => FreeLibrary(handle);
}
