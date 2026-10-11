"""Safety checks for a single-child opt-in write (no real network calls)."""
import unittest
from tuya_color_test import (
    TEST_HSV,
    decode_hsv,
    encode_hsv,
    run_test,
    verify_bulb_ready,
)

ENV = {
    "TUYA_GATEWAY_IP": "192.0.2.55",
    "TUYA_GATEWAY_ID": "example-gateway",
    "TUYA_GATEWAY_LOCAL_KEY": "example-key",
    "TUYA_CHILD_ID": "example-child",
    "TUYA_CHILD_CID": "example-cid",
}


class FakeGateway:
    def __init__(self):
        self.closed = False

    def close(self):
        self.closed = True


class FakeBulb:
    def __init__(self, state=None):
        self.dps = dict(state or {"1": True, "2": "colour", "5": "00df03e803e8"})
        self.writes = []

    def status(self):
        return {"dps": dict(self.dps)}

    def set_value(self, index, value):
        self.writes.append((index, value))
        self.dps[str(index)] = value
        return {"dps": {str(index): value}}


class FakeTinyTuya:
    def __init__(self, state=None):
        self.gateway = FakeGateway()
        self.bulb = FakeBulb(state)
        self.calls = []

    def Device(self, *args, **kwargs):
        self.calls.append((args, kwargs))
        if "parent" in kwargs:
            assert kwargs["cid"] == ENV["TUYA_CHILD_CID"]
            assert kwargs["parent"] is self.gateway
            return self.bulb
        assert args == (ENV["TUYA_GATEWAY_ID"], ENV["TUYA_GATEWAY_IP"], ENV["TUYA_GATEWAY_LOCAL_KEY"])
        assert kwargs["version"] == 3.5
        return self.gateway


class OneShotColorTests(unittest.TestCase):
    def test_decode_samples_from_real_bulb(self):
        self.assertEqual(decode_hsv("015d03e803e8"), (349, 1000, 1000))
        self.assertEqual(decode_hsv("00df03e803e8"), (223, 1000, 1000))

    def test_green_at_40_percent(self):
        self.assertEqual(encode_hsv(120, 1000, 400), TEST_HSV)
        self.assertEqual(decode_hsv(TEST_HSV), (120, 1000, 400))

    def test_dry_run_never_writes(self):
        t = FakeTinyTuya()
        result = run_test(ENV, apply=False, device_module=t)
        self.assertEqual(result, 0)
        self.assertEqual(t.bulb.writes, [])
        self.assertTrue(t.gateway.closed)

    def test_confirmation_rejects_unintended_write(self):
        t = FakeTinyTuya()
        result = run_test(ENV, apply=True, device_module=t, confirmation=lambda _: "no")
        self.assertEqual(result, 2)
        self.assertEqual(t.bulb.writes, [])

    def test_one_verified_green_write(self):
        t = FakeTinyTuya()
        result = run_test(ENV, apply=True, device_module=t, confirmation=lambda _: "GREEN", pause=lambda _: None)
        self.assertEqual(result, 0)
        self.assertEqual(t.bulb.writes, [(5, TEST_HSV)])
        self.assertEqual(t.bulb.dps["1"], True)
        self.assertEqual(t.bulb.dps["2"], "colour")
        self.assertTrue(t.gateway.closed)

    def test_refuse_off_and_white_modes(self):
        for state in (
            {"1": False, "2": "colour", "5": "00df03e803e8"},
            {"1": True, "2": "white", "5": "00df03e803e8"},
            {"1": True, "2": "colour"},
        ):
            with self.subTest(state=state):
                t = FakeTinyTuya(state)
                self.assertEqual(run_test(ENV, apply=True, device_module=t, confirmation=lambda _: "GREEN"), 3)
                self.assertEqual(t.bulb.writes, [])

    def test_missing_secret_never_connects(self):
        t = FakeTinyTuya()
        missing = dict(ENV)
        missing.pop("TUYA_GATEWAY_LOCAL_KEY")
        self.assertEqual(run_test(missing, apply=True, device_module=t), 2)
        self.assertEqual(t.calls, [])

    def test_reject_bad_hsv(self):
        for invalid in ("123", "ffff03e803e8", "00ff03e80fff", None):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                decode_hsv(invalid)


if __name__ == "__main__":
    unittest.main()
