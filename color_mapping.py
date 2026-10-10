"""Pure RGB -> Tuya 12-character HSV DPS 5 conversion (no device IO)."""
from __future__ import annotations

import colorsys


def rgb_to_tuya_hsv(r: int, g: int, b: int, brightness_cap: int = 400) -> str:
    """Return HHHHSSSSVVVV; brightness_cap=0..1000, default 40%.

    Input brightness scales rather than clipping, preserving darker colors.
    """
    if any(type(x) is not int or not 0 <= x <= 255 for x in (r, g, b)):
        raise ValueError("RGB channels must be integers in the range 0..255")
    if type(brightness_cap) is not int or not 0 <= brightness_cap <= 1000:
        raise ValueError("Brightness cap must be an integer in the range 0..1000")
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    return f"{round(h * 360) % 360:04x}{round(s * 1000):04x}{round(v * brightness_cap):04x}"
