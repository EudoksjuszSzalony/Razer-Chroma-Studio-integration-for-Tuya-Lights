import unittest

from bridge_preview import RGB, FrameThrottle, decode_frame
from gateway_probe import safe_status_summary


class BridgeTests(unittest.TestCase):
    def test_read_five_zones(self):
        s = '{"type":"colors","rgb":[' + ','.join(['{"r":1,"g":2,"b":3}'] * 5) + ']}'
        frame = decode_frame(s)
        self.assertEqual(len(frame), 5)
        self.assertEqual(frame[0].hex(), "#010203")

    def test_reject_bad_count(self):
        with self.assertRaises(ValueError):
            decode_frame('{"type":"colors","rgb":[]}')

    def test_reject_bad_channels(self):
        for bad in (256, -1, True, "20", 1.5):
            with self.subTest(value=bad), self.assertRaises(ValueError):
                RGB.decode({"r": bad, "g": 2, "b": 3})

    def test_ignore_log_lines_and_other_messages(self):
        self.assertIsNone(decode_frame("app starting"))
        self.assertIsNone(decode_frame('{"type":"status","state":"ok"}'))

    def test_rate_limit_and_deduplicate(self):
        frame1 = (RGB(0, 0, 0),) * 5
        frame2 = (RGB(1, 2, 3),) * 5
        t = FrameThrottle(2)
        self.assertTrue(t.accept(frame1, 0.0))
        self.assertFalse(t.accept(frame1, 1.0))
        self.assertFalse(t.accept(frame2, 0.2))
        self.assertTrue(t.accept(frame2, 0.5))

    def test_sanitize_response_no_keys(self):
        data = {"local_key":"dont-print", "device_id":"redact", "dps":{"1":True,"2":"rgb","3":{"local_key":"secret"}}}
        result = safe_status_summary(data)
        self.assertEqual(result["dps"], {"1":True,"2":"rgb"})
        self.assertNotIn("secret", str(result))
        self.assertNotIn("dont-print", str(result))


if __name__ == "__main__":
    unittest.main()
