"""Offline-only RGB -> HSV tests; no bulb or Chroma SDK required."""
import unittest

from color_mapping import rgb_to_tuya_hsv


class RGBToTuyaTests(unittest.TestCase):
    def test_green_matches_verified_40_percent_write(self):
        self.assertEqual(rgb_to_tuya_hsv(0, 255, 0), "007803e80190")

    def test_primaries(self):
        self.assertEqual(rgb_to_tuya_hsv(255, 0, 0), "000003e80190")
        self.assertEqual(rgb_to_tuya_hsv(0, 0, 255), "00f003e80190")

    def test_black_and_white(self):
        self.assertEqual(rgb_to_tuya_hsv(0, 0, 0), "000000000000")
        self.assertEqual(rgb_to_tuya_hsv(255, 255, 255, 250), "0000000000fa")

    def test_dim_color_preserves_brightness(self):
        self.assertEqual(rgb_to_tuya_hsv(0, 128, 0), "007803e800c9")

    def test_invalid_rgb_and_brightness(self):
        for channels in ((True, 0, 0), (-1, 0, 0), (256, 0, 0), (1.5, 0, 0)):
            with self.subTest(rgb=channels), self.assertRaises(ValueError):
                rgb_to_tuya_hsv(*channels)
        for cap in (-1, 1001, True, 4.5):
            with self.subTest(cap=cap), self.assertRaises(ValueError):
                rgb_to_tuya_hsv(0, 255, 0, cap)


if __name__ == "__main__":
    unittest.main()
