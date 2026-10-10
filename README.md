# Chroma Studio × Tuya Lights

**Experimental bridge to synchronize Tuya/Smart Life lighting with custom Razer Chroma Studio profiles.**

[![Build](https://github.com/EudoksjuszSzalony/Razer-Chroma-Studio-integration-for-Tuya-Lights/actions/workflows/ci.yml/badge.svg)](https://github.com/EudoksjuszSzalony/Razer-Chroma-Studio-integration-for-Tuya-Lights/actions/workflows/ci.yml)
![Status](https://img.shields.io/badge/status-protocol%20research-orange)
![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![MIT](https://img.shields.io/badge/license-MIT-green)

> **Experimental, not yet end-to-end Chroma sync.** A SilverCrest Tuya 3.5 gateway accepted an authenticated Zigbee child status read and one opt-in color write. Live Synapse RGB events, physical lamp color and sustained update rate are not yet verified.

## Goal

Play the **same custom advanced effects configured in Chroma Studio** on selected Tuya RGB/RGBW Zigbee bulbs, without disabling Synapse lighting on Razer peripherals and without removing or replacing the existing Smart Life gateway.

```text
Synapse 3 → Chroma Connect color events (5 virtual zones)
                  ↓
       RGB zone mapping / rate limiter
                  ↓
   Tuya LAN protocol 3.5 gateway adapter
                  ↓
        Zigbee RGB/RGBW bulbs
```

Both interfaces have important limits. **The ordinary Razer Chroma REST SDK sends effects; it does not read the current Studio output.** We are exploring the Chroma Connect *device/partner* API instead, using the MIT-licensed [ChromaControl Synapse SDK](https://github.com/ChromaControl/SDK). That SDK exposes five incoming color values, but requires Razer's native `RzChromaConnectAPI64.dll` and an application ID accepted by Synapse. We will not impersonate another vendor's app.

A LAN TCP connection alone does not prove Zigbee support. The tested SilverCrest gateway has now accepted one authenticated status read and one DPS 5 HSV write with successful state readback. Other hubs, child devices and repeated fast updates are unverified.

## First tests — no changes to Smart Life

### 1. Test that the gateway is reachable (read-only)

Python 3.11+ is sufficient for the basic TCP test. **No secret key is required.**

```powershell
py gateway_probe.py --host 192.168.1.100
```

The example IP is only a placeholder for your own gateway. This checks TCP port 6668 and does not send any Tuya control commands.

### 2. Obtain the key locally and check a Zigbee child (read-only)

TinyTuya can obtain the *gateway* local key and child metadata through an authorized Tuya IoT project linked to your Smart Life account:

```powershell
py -m pip install -r requirements.txt
py -m tinytuya wizard
```

See [TinyTuya documentation](https://github.com/jasonacox/tinytuya/blob/master/API.md). Do **not** upload `devices.json`, `tinytuya.json`, Local Keys, Tuya Cloud secrets, or raw unredacted diagnostic files to GitHub.

In the *current PowerShell session*, set the credentials privately (example names):

```powershell
$env:TUYA_GATEWAY_IP = '192.168.1.100'
$env:TUYA_GATEWAY_ID = '<gateway-device-id>'
$env:TUYA_GATEWAY_LOCAL_KEY = '<gateway-local-key>'
$env:TUYA_CHILD_ID = '<bulb-device-id>'
$env:TUYA_CHILD_CID = '<bulb-node-id-or-cid>'

py gateway_probe.py --read-child
```

`--read-child` makes an authenticated **status query only**; it never toggles a light. Some Lidl/Tuya gateways may not respond to local sub-device reads. Do not re-pair or flash the hub. Clear secrets when done: `Remove-Item Env:TUYA_GATEWAY_LOCAL_KEY`.

### 3. Inspect a sample Chroma event (no bulbs controlled)

```powershell
py bridge_preview.py --demo
```

The preview understands five RGB zone colors, provides color conversion and low-frequency deduplication suitable for future Zigbee control, and **never sends packets to the gateway**.

### 4. Optional: native Synapse color reception probe (Windows)

Requires .NET 8 SDK, Synapse 3 with Chroma Connect, an installed/accessible `RzChromaConnectAPI64.dll` and a valid app ID issued/authorized for the Chroma Connect device integration.

```powershell
$env:CHROMACONTROL_KEY_SYNAPSE = '<your-authorized-chroma-connect-app-guid>'
dotnet run --project src/ChromaProbe/ChromaProbe.csproj
```

It prints five-color JSONL frames to stdout; it does not alter Studio profiles or lighting. Without an authorized ID or native Razer library it exits with an explicit diagnostic. If your device is not registered with Chroma Connect, this may not work.

The **developer API route is not the same** as an ordinary Chroma SDK "game/app" registration. Razer's [Connected Devices program](https://developer.razer.com/connected-devices/) is the supported route for a recognized third-party device.

## Single-bulb LAN color test (opt-in)

Real-world read-only tests of a SilverCrest Tuya 3.5 gateway and a Zigbee **ceiling bulb** confirmed that `DPS 5` is a 12-digit HSV string: a red sample decoded to (349°, 1000, 1000) and a blue sample to (223°, 1000, 1000).

The **experimental write test** is limited to the same bulb. It refuses to proceed unless the bulb is already **ON** and in **COLOUR** mode; prepare those settings in Smart Life. It writes only `DPS 5` once (no power, mode or other devices), and requires an explicit `GREEN` confirmation. It leaves Smart Life pairing untouched.

**First, with the same PowerShell session/environment variables as the successful read-only probe:**

```powershell
py tuya_color_test.py
```

This is the default read-only dry run. If it reports the expected ceiling bulb state and you agree to change its color, run:

```powershell
py tuya_color_test.py --apply
```

Type `GREEN` when prompted. Target is green at 40% HSV value: `007803e80190`. Observe the actual lamp and the new DPS 5 read-back. Afterwards you can select any color again with Smart Life. **Do not run in a loop**, and do not upload logs containing keys or device IDs. The real test returned `SUCCESS` after writing and reading back HSV (120°, 1000, 400); the visible color of the physical lamp still requires confirmation.

## Preview Chroma RGB to Tuya HSV (offline)

The five incoming Chroma Connect zones can be previewed as a 12-character Tuya HSV DPS 5 value without sending anything to a light. The default brightness cap is 40%, based on the first confirmed color-write/readback test.

```powershell
py bridge_preview.py --demo --tuya-preview --zone 1 --max-brightness 40
```

Once Chroma Connect is registered and producing actual five-zone RGB events, the JSONL output may be piped to this preview:

```powershell
dotnet run --project src/ChromaProbe/ChromaProbe.csproj | py bridge_preview.py --tuya-preview --zone 1
```

These commands do not control any bulbs. A successful preview does not validate Synapse connectivity or an authorized Chroma Connect app GUID.

## Planned features

- Multiple bulbs, each mapped to one Chroma zone or a weighted average.
- Optional groups, per-bulb brightness caps, RGBW handling, color smoothing and automatic reconnection.
- Conservative Zigbee update rate (starting at 2 updates/second per bulb), to avoid saturating the gateway.
- Safe defaults, local-only configuration and Windows installer/tray integration once hardware compatibility is confirmed.
- Never take control of the user's existing Razer peripherals or reset the Smart Life pairing.

## Status and limitations

| Area | Current state |
| --- | --- |
| Tuya UDP discovery / TCP connectivity | Observed with a test gateway |
| Tuya 3.5 authenticated session | Successful on tested SilverCrest gateway |
| Zigbee child status and HSV DPS | Read + one write/readback confirmed; visible color pending |
| Synapse 3 custom Studio color reception | Candidate MIT SDK identified; requires native DLL and authorized app ID |
| Real-time lighting sync | Not implemented; offline Chroma RGB-to-HSV preview available |

See [Architecture and protocol notes](docs/ARCHITECTURE.md).

## Development

```powershell
py -m unittest discover -s tests -v
dotnet build src/ChromaProbe/ChromaProbe.csproj
```

## License

MIT. See [LICENSE](LICENSE). Independent community project, not affiliated with Razer or Tuya. All trademarks belong to their owners.
