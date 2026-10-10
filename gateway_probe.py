"""Non-destructive LAN and Zigbee-child status probe for Tuya 3.5 gateways.

No control/set_value/turn_on/turn_off calls are made. Credentials come only
from environment variables, never command-line arguments or source files.
"""
from __future__ import annotations

import argparse
import os
import socket
import sys
from typing import Any

DEFAULT_PORT = 6668


def tcp_check(host: str, port: int = DEFAULT_PORT, timeout: float = 3.0) -> bool:
    """Only establish a TCP socket; do not transmit application data."""
    with socket.create_connection((host, port), timeout=timeout):
        return True


def safe_status_summary(data: Any) -> dict[str, Any]:
    """Only display DPS data and an allow-list of error/status fields."""
    if not isinstance(data, dict):
        return {"status": "no structured response"}
    result: dict[str, Any] = {}
    if isinstance(data.get("dps"), dict):
        for k, value in data["dps"].items():
            # Avoid accidentally emitting sensitive nested payloads.
            if isinstance(value, (str, int, float, bool, type(None))) and len(str(value)) <= 160:
                result.setdefault("dps", {})[str(k)] = value
    for key in ("Error", "Err", "error"):
        if key in data and isinstance(data[key], (str, int)):
            result[key] = str(data[key])[:160]
    return result or {"status": "gateway responded without DPS fields"}


def read_child_status(
    host: str,
    gateway_id: str,
    local_key: str,
    child_id: str,
    child_cid: str,
    version: float = 3.5,
) -> dict[str, Any]:
    """Perform a single authenticated read of the Zigbee child."""
    import tinytuya  # installed only for the optional read-only probe

    gateway = tinytuya.Device(gateway_id, host, local_key, persist=True, version=version)
    try:
        child = tinytuya.Device(child_id, cid=child_cid, parent=gateway)
        return safe_status_summary(child.status())
    finally:
        close = getattr(gateway, "close", None)
        if callable(close):
            close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Tuya gateway read-only diagnostics")
    parser.add_argument("--host", default=os.environ.get("TUYA_GATEWAY_IP"), help="Gateway LAN IP (or TUYA_GATEWAY_IP)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--timeout", type=float, default=3.0)
    parser.add_argument("--read-child", action="store_true", help="One authenticated Zigbee child status query")
    args = parser.parse_args(argv)
    if not args.host:
        parser.error("Specify --host or TUYA_GATEWAY_IP")

    print(f"Gateway LAN: {args.host}:{args.port} | TCP connection only")
    try:
        tcp_check(args.host, args.port, args.timeout)
    except (OSError, TimeoutError):
        print("TCP connection failed (check IP, port, and firewall).")
        return 1
    print("TCP connection successful. This does not prove Tuya authentication or Zigbee control.")

    if not args.read_child:
        print("Read-only TCP probe complete. No commands sent.")
        return 0

    names = ("TUYA_GATEWAY_ID", "TUYA_GATEWAY_LOCAL_KEY", "TUYA_CHILD_ID", "TUYA_CHILD_CID")
    missing = [n for n in names if not os.environ.get(n)]
    if missing:
        print("Cannot query child; missing environment variable names: " + ", ".join(missing))
        return 2
    try:
        details = read_child_status(args.host, *(os.environ[n] for n in names))
    except ImportError:
        print("Install TinyTuya first: py -m pip install -r requirements.txt")
        return 2
    except Exception as exc:
        # Do not print the native exception: it may expose IDs / local keys.
        print("Authenticated child read failed (" + type(exc).__name__ + ").")
        print("This gateway may ignore local queries even with valid credentials.")
        return 3
    print("Authenticated child response (filtered): " + str(details))
    return 0 if "dps" in details else 3


if __name__ == "__main__":
    sys.exit(main())
