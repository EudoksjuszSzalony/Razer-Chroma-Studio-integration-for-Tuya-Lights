# Native Windows Hue Bridge — Phase 1 (Discovery)

This is an **experimental local emulator**, not a complete Philips Hue Bridge.
The first test is whether **Razer Synapse 3 > HUE** discovers a virtual
bridge running on the Windows PC and optionally pairs with one simulated lamp.
The prototype requires only **C#/.NET 9**, no Docker, no OpenRGB and no
real Tuya connections.

## Implemented

- SSDP IPv4 multicast listener on UDP 1900 answering Hue M-SEARCH packets.
- HTTP listening only on an explicitly chosen IPv4 LAN interface,
  by default on TCP port 80, including /description.xml.
- A **partial** Hue v1 HTTP API: /api/nouser/config, /api, /api/{user}/lights,
  /api/{user}/groups and a virtual Entertainment group with one light.
- A deliberately short pairing window (flag --pairing-seconds, 0 by default,
  maximum 300s). POST /api otherwise reports that the link button wasn't pressed.
- Simulated light-state updates IN MEMORY ONLY. No control of physical bulbs.
- A persistent bridge ID and local client credentials in
  %LOCALAPPDATA%\ChromaTuyaBridge\hue-discovery.json. Do not upload it or
  publish tokens; it can authorize clients to the local test Hue API.

**NOT IMPLEMENTED**: HTTPS/CLIP v2, Hue Entertainment DTLS (UDP 2100),
actual Studio RGB streaming, automatic Tuya bridge discovery or full GUI.
If a client requests to start the entertainment stream, the API returns an
**explicit error**. Seeing a virtual bridge in Synapse is not proof of
real-time Chroma effect compatibility.

## 1. Find the LAN IP of the WINDOWS PC

In PowerShell:

    Get-NetIPAddress -AddressFamily IPv4 |
        Where-Object { $_.IPAddress -like '192.168.1.*' } |
        Select-Object IPAddress, InterfaceAlias

Use the address of your **Windows PC**, e.g. 192.168.1.20, not the Tuya
gateway address 192.168.1.100. VM, VPN and virtual Ethernet adapters may
have different addresses.

## 2. Start the emulator for discovery only

From the project folder:

    dotnet run --project src\HueDiscoveryProbe\HueDiscoveryProbe.csproj -- --ip 192.168.1.20

Replace the example IP with your Windows machine address. Default TCP/80
and UDP/1900 must be free. If Windows Firewall asks, permit on a **Private**
network only. Do not forward these ports to the internet.

For local HTTP troubleshooting only, you can use --port 8088. However, some
Hue clients require port 80, so a nonstandard port may prevent discovery.
Windows' own SSDP Discovery service may share or interfere with UDP 1900;
if the console reports an SSDP error, share the error before changing any
system services.

## 3. Verify locally and open Synapse

In a second PowerShell:

    Invoke-WebRequest http://192.168.1.20/description.xml -UseBasicParsing |
        Select-Object StatusCode, Content

    Invoke-RestMethod http://192.168.1.20/api/nouser/config |
        Select-Object name, bridgeid, modelid

Go to **Synapse 3 > HUE** and look for a Hue bridge. The console will print
[SSDP] lines if the module sends supported discovery requests. Send only
those lines and what Synapse displays. No secret keys are needed.

## 4. Optional: short pairing window

After **discovery** is successful, press Ctrl+C in the emulator and restart:

    dotnet run --project src\HueDiscoveryProbe\HueDiscoveryProbe.csproj -- --ip 192.168.1.20 --pairing-seconds 120

Within 120 seconds, follow the Synapse pairing prompt. The virtual light
does not affect Smart Life or real devices. Credentials persist across
restarts in the user profile.

## 5. Offline smoke-test mode

Runs without SSDP, without admin rights or port 80:

    dotnet run --project src\HueDiscoveryProbe\HueDiscoveryProbe.csproj -- --ip 127.0.0.1 --port 18080 --no-ssdp --pairing-seconds 120

Then open http://127.0.0.1:18080/description.xml.
CI runs scripts/test_hue_probe.ps1 on Windows using this mode, but does not
replicate Synapse itself.

## Technical references

- https://github.com/diyhue/diyHue/blob/master/BridgeEmulator/services/ssdp.py
- https://github.com/diyhue/diyHue/blob/master/BridgeEmulator/flaskUI/templates/description.xml
- https://mysupport.razer.com/app/answers/detail/a_id/4054/

Independent community project under the MIT License.
