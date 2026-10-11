# Discovery from Synapse Hue — network trace findings (2026-10-11)

The user captured `synapse_hue_1.pcapng.lz4` on **Marvell AQC111C 5GbE**
(interface link type Ethernet, capture about 36.12 seconds, 227 packets).
Do **not** upload the original capture to a public repository; it contains
unrelated device traffic and network addresses.

Relevant observations from manual pcapng decode:

- Frames **31, 32, 38**: IPv4 mDNS PTR queries from the Windows PC for
  `_hue._tcp.local` to `224.0.0.251:5353`; other simultaneous mDNS queries
  included `_hap._tcp.local` and `_sleep-proxy._udp.local`. No Hue mDNS
  response is present in this captured physical-interface trace.
- Frames **59–61, 84**: DNS A lookup of
  `discovery.meethue.com`; frames **62–96** include subsequent TLS 443
  communication with `34.117.13.189`, and ClientHello SNI in frame **65**
  confirms `discovery.meethue.com`. TLS application content is encrypted.
  Capture metadata by itself does **not** establish which executable made
  each network request.
- Frames **131, 144, 147, 153**: SSDP M-SEARCH with
  `ST: urn:dial-multiscreen-org:service:dial:1` (DIAL, not Hue discovery).
  Prior SSDP Hue responder deliberately ignores these unrelated requests.
- The capture has only **one interface**, Marvell physical Ethernet.
  Same-host traffic between Synapse and the emulator at `192.168.1.10`
  may bypass the physical adapter. Absence of local HTTP/HTTPS on that
  interface is **not** proof that Synapse never tried them.

Changes motivated by the trace:

- Added a **Windows-native mDNS responder** on UDP 5353 for `_hue._tcp.local`,
  matching the basic service metadata described by
  [diyHue](https://github.com/diyhue/diyHue/blob/master/BridgeEmulator/services/mdns.py).
- Advertises a synthetic Hue v1-compatible bridge on the actual PC LAN IP,
  providing PTR/SRV/TXT/A records. No physical Tuya device writes.
- Logs query receipt and responds using multicast on the selected interface.
- Includes an offline in-memory packet self-test and CLI `--no-mdns` for
  isolating HTTP/SSDP tests.
- This **does not implement** Hue HTTPS API v2, cloud discovery, or
  Entertainment DTLS. Synapse detection still needs real-world verification.
