"""Preview five Chroma Connect RGB zones without sending anything to Tuya."""
from __future__ import annotations

import argparse
import json
import sys
import time
from dataclasses import dataclass

from color_mapping import rgb_to_tuya_hsv


@dataclass(frozen=True)
class RGB:
    r: int
    g: int
    b: int

    @classmethod
    def decode(cls, value: object) -> "RGB":
        if not isinstance(value, dict):
            raise ValueError("Each color must be an object with r, g and b.")
        vals = [value.get(key) for key in ("r", "g", "b")]
        if any(type(v) is not int or not 0 <= v <= 255 for v in vals):
            raise ValueError("Each RGB channel must be an integer in 0..255.")
        return cls(*vals)

    def hex(self) -> str:
        return f"#{self.r:02X}{self.g:02X}{self.b:02X}"


def decode_frame(raw: str) -> tuple[RGB, ...] | None:
    try:
        data = json.loads(raw)
    except ValueError:
        return None  # ignore non-JSON startup output
    if not isinstance(data, dict) or data.get("type") != "colors":
        return None
    colors = data.get("rgb")
    if not isinstance(colors, list) or len(colors) != 5:
        raise ValueError("Chroma Connect frame must have exactly five RGB zones.")
    return tuple(RGB.decode(c) for c in colors)


class FrameThrottle:
    """Ignore duplicate frames; cap future Zigbee traffic (preview only)."""
    def __init__(self, updates_per_second: float = 2.0) -> None:
        if not 0 < updates_per_second <= 20:
            raise ValueError("updates_per_second must be >0 and <=20")
        self.interval = 1.0 / updates_per_second
        self.last_time = float("-inf")
        self.last_frame: tuple[RGB, ...] | None = None

    def accept(self, frame: tuple[RGB, ...], now: float) -> bool:
        if frame == self.last_frame or now - self.last_time < self.interval:
            return False
        self.last_time, self.last_frame = now, frame
        return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only five-zone color event viewer")
    parser.add_argument("--demo", action="store_true", help="Emit a single artificial event, no Synapse needed")
    parser.add_argument("--updates-per-second", type=float, default=2.0)
    parser.add_argument("--tuya-preview", action="store_true",
                        help="Print selected zone as a Tuya HSV string; never sends commands")
    parser.add_argument("--zone", type=int, choices=range(1, 6), default=1,
                        help="Zone for DPS 5 preview, 1..5 (default 1)")
    parser.add_argument("--max-brightness", type=int, choices=range(0, 101), default=40,
                        help="Preview brightness cap 0..100 percent (default 40)")
    args = parser.parse_args(argv)
    throttle = FrameThrottle(args.updates_per_second)
    if args.demo:
        lines = [json.dumps({"type": "colors", "rgb": [
            {"r": 255, "g": 0, "b": 0}, {"r": 255, "g": 127, "b": 0},
            {"r": 255, "g": 255, "b": 0}, {"r": 0, "g": 255, "b": 0},
            {"r": 0, "g": 0, "b": 255},
        ]})]
    else:
        print("Reading Chroma JSONL events from stdin (no lights controlled).", file=sys.stderr)
        lines = sys.stdin

    for line in lines:
        try:
            frame = decode_frame(line)
            if frame is not None and throttle.accept(frame, time.monotonic()):
                print("5 zones: " + " | ".join(c.hex() for c in frame), flush=True)
                if args.tuya_preview:
                    color = frame[args.zone - 1]
                    hsv = rgb_to_tuya_hsv(color.r, color.g, color.b, args.max_brightness * 10)
                    print(f"Tuya DPS 5 preview for zone {args.zone}: {hsv}", flush=True)
        except ValueError as exc:
            print("Invalid RGB frame: " + str(exc), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
