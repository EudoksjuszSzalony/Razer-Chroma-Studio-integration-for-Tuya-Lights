"""Opt-in, one-shot GREEN command to a *single* verified Tuya Zigbee bulb.

Defaults to read-only dry-run. Requires explicit --apply and typed GREEN.
Only DPS 5 is written; no power/mode changes, repeats or background loops.
"""
from __future__ import annotations

import argparse
import os
import re
import sys
import time
from typing import Any, Callable

# Hue 120 degrees (green), saturation 100%, brightness 40%.
TEST_HSV = "007803e80190"
ENV_NAMES = (
    "TUYA_GATEWAY_IP",
    "TUYA_GATEWAY_ID",
    "TUYA_GATEWAY_LOCAL_KEY",
    "TUYA_CHILD_ID",
    "TUYA_CHILD_CID",
)


def encode_hsv(hue: int, saturation: int, value: int) -> str:
    if not (0 <= hue <= 359 and 0 <= saturation <= 1000 and 0 <= value <= 1000):
        raise ValueError("HSV outside Tuya supported range")
    return f"{hue:04x}{saturation:04x}{value:04x}"


def decode_hsv(encoded: object) -> tuple[int, int, int]:
    if not isinstance(encoded, str) or not re.fullmatch(r"[0-9a-fA-F]{12}", encoded):
        raise ValueError("DPS 5 is not a 12-digit hex HSV value")
    hue, sat, val = (int(encoded[i:i + 4], 16) for i in (0, 4, 8))
    if hue > 359 or sat > 1000 or val > 1000:
        raise ValueError("DPS 5 contains invalid HSV components")
    return hue, sat, val


def verify_bulb_ready(payload: Any) -> dict:
    """Fail closed: unknown/missing DPS or non-color mode may not be written."""
    if not isinstance(payload, dict) or not isinstance(payload.get("dps"), dict):
        raise ValueError("No readable DPS data for the selected Zigbee child")
    dps = payload["dps"]
    if dps.get("1") is not True:
        raise ValueError("Target bulb is not ON; switch it on in Smart Life first")
    if dps.get("2") != "colour":
        raise ValueError("Target bulb is not in COLOUR mode; select a color in Smart Life first")
    decode_hsv(dps.get("5"))
    return dps


def run_test(
    env: dict[str, str],
    *,
    apply: bool,
    device_module: Any,
    confirmation: Callable[[str], str] = input,
    pause: Callable[[float], None] = time.sleep,
) -> int:
    missing = [n for n in ENV_NAMES if not env.get(n)]
    if missing:
        print("Missing environment variable names: " + ", ".join(missing))
        return 2

    gateway = device_module.Device(
        env["TUYA_GATEWAY_ID"],
        env["TUYA_GATEWAY_IP"],
        env["TUYA_GATEWAY_LOCAL_KEY"],
        persist=True,
        version=3.5,
    )
    try:
        bulb = device_module.Device(
            env["TUYA_CHILD_ID"],
            cid=env["TUYA_CHILD_CID"],
            parent=gateway,
        )
        try:
            original = verify_bulb_ready(bulb.status())
        except ValueError as exc:
            print("Safety stop: " + str(exc))
            return 3
        h, s, v = decode_hsv(original["5"])
        print(f"Confirmed bulb ON, COLOUR mode. Current HSV: H={h}, S={s}, V={v}.")
        print("Target: GREEN (H=120, S=1000, V=400); DPS 5 ONLY.")
        print("No power toggles, mode changes, other bulbs, or repeated writes.")

        if not apply:
            print("DRY RUN: no commands sent. Add --apply to request one color change.")
            return 0

        if confirmation("To send one GREEN frame, type GREEN and press Enter: ").strip() != "GREEN":
            print("Canceled. Nothing was written.")
            return 2

        # Official TinyTuya child API, not a direct gateway control call.
        # Exactly one command, with no automatic repeated writes.
        bulb.set_value(5, TEST_HSV)
        print("One color command sent. Checking the resulting state...")
        pause(1.0)
        check = bulb.status()
        try:
            new_dps = verify_bulb_ready(check)
        except ValueError:
            print("State verification inconclusive; inspect the bulb in Smart Life.")
            return 3
        if new_dps["5"].lower() == TEST_HSV:
            print("SUCCESS: bulb reports HSV (120, 1000, 400).")
            return 0
        h, s, v = decode_hsv(new_dps["5"])
        print(f"No exact confirmation: bulb now reports H={h}, S={s}, V={v}.")
        print("Check Smart Life and report the observation; do not run in a loop.")
        return 3
    finally:
        close = getattr(gateway, "close", None)
        if callable(close):
            close()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="One-shot green Zigbee bulb test (read-only by default)")
    parser.add_argument("--apply", action="store_true", help="Request one write after explicit GREEN confirmation")
    args = parser.parse_args(argv)
    try:
        import tinytuya
        return run_test(dict(os.environ), apply=args.apply, device_module=tinytuya)
    except ImportError:
        print("TinyTuya is missing. Run: py -m pip install -r requirements.txt")
        return 2
    except (OSError, RuntimeError, ValueError) as exc:
        # Never echo native driver exceptions, which may include credentials.
        print("Connection or command failed (" + type(exc).__name__ + ").")
        return 3
    except Exception as exc:
        print("Unexpected device error (" + type(exc).__name__ + ").")
        return 3


if __name__ == "__main__":
    sys.exit(main())
