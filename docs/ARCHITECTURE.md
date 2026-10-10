# Architecture and protocol research

## Requirements

- **Custom Chroma Studio advanced effects**, not merely Static/Breathing presets.
- Existing **Synapse 3** and its other devices must not be controlled by this application.
- Keep **Smart Life**, existing Tuya Zigbee pairing and stock SilverCrest firmware intact.
- No keys, identifiers tied to an individual account, or proprietary partner IDs in Git history.

## Chroma inbound bridge

**Important distinction:** the public Chroma SDK REST/WebSocket API is primarily for applications to **write** lighting effects. Merely opening an SDK client session can take priority over Studio. This is not the required path.

Chroma Connect's partner/device API can deliver RGB values **from Synapse to third-party software**, including advanced effects. The MIT-licensed ChromaControl.SDK.Synapse uses `RzChromaConnectAPI64.dll`, an app GUID and a native event callback. Each callback carries **five** color zones. See:

- https://github.com/ChromaControl/SDK
- https://github.com/ChromaControl/SDK/blob/main/src/ChromaControl.SDK.Synapse/SynapseService.cs
- https://developer.razer.com/connected-devices/
- https://mysupport.razer.com/app/answers/detail/a_id/1906/

`src/ChromaProbe` shows how to receive the events, **provided** a compatible native library and authorized partner app GUID are available. This repository does not bundle `RzChromaConnectAPI64.dll` or re-use another manufacturer's GUID.

The five colors do **not** encode the entire per-LED keyboard scene. For lighting zones, map each Zigbee light to a zone or weighted combination. A room-sized moving effect can be approximated spatially, but not guaranteed to reproduce arbitrary per-key profiles perfectly.

## Tuya inbound/outbound bridge

A gateway broadcasting as **Tuya 3.5** on UDP and listening on TCP 6668 is only an initial connectivity indicator. Authenticated LAN access requires the **gateway local key**.

TinyTuya can create a gateway `Device(id, ip, key, version=3.5)` and a child `Device(child_id, cid=child_cid, parent=gateway)`. Only once child `status()` works and DPS fields are mapped should we test sending colors.

- https://github.com/jasonacox/tinytuya/blob/master/API.md
- https://github.com/jasonacox/tinytuya/blob/master/PROTOCOL.md
- https://github.com/jasonacox/tinytuya/discussions/470

Some Lidl 3.5 hubs reject `gateway.status()` or child queries despite a successful session handshake. There is **no guarantee** this particular model forwards color commands locally. In that case the fallback is a Tuya Cloud bridge (higher latency) or a different local Zigbee coordinator (would affect pairing and is NOT part of the default design).

For safety, color writes require user opt-in after verifying the target child ID, CIDs, on/off DPS, RGB color data encoding and capability. Do not write arbitrary DPS guesses.

## Planned data path

```text
Chroma Connect native events (up to five RGB zones)
        │
        ▼
Frame queue (keep latest; no unbounded buildup)
        │
        ▼
Per-bulb mapping / brightness limit / RGBW conversion
        │
        ▼
Per-bulb throttle (start at 2 Hz; skip duplicate colors)
        │
        ▼
Authenticated Tuya 3.5 gateway + Zigbee children
```

The gateway often cannot update Zigbee bulbs at the same frame rate as a directly connected RGB keyboard. Rate limiting will be necessary and animation fidelity will depend on LAN/Zigbee latency and device firmware.

## Test gates

1. LAN port reachable without credentials.
2. Authenticated **read-only** status of at least one Zigbee bulb through the existing gateway.
3. Read-only five-zone **live** color events change when a custom Studio profile changes.
4. Map a **single** bulb color with explicit permission, first at low frequency.
5. Measure lag and add per-bulb configs; only then create installer/tray support.

No firmware flashing, packet sniffing, profile modification, or pretending to be a registered lighting partner in these probes.
