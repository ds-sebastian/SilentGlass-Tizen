"""Notifications on the TV: Home Assistant's own client (notifications-android-tv 0.1.5, the version the nfandroidtv
integration pins) against the bridge's request reader and multipart parser, run off the TV through
tests/notice-harness. Skipped unless the harness is built (.local/notice-harness) and the client library is importable:

    dotnet build tests/notice-harness/NoticeHarness.csproj -c Release -o .local/notice-harness
    uv run --with notifications-android-tv==0.1.5 python -m unittest tests.test_s90d_notice
"""
import json
import os
import shutil
import subprocess
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HARNESS = ROOT / ".local/notice-harness/NoticeHarness.dll"
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet") or "dotnet"
SAMPLE = ROOT / ".local/notice-mock/sample.jpg"
SRC = ROOT / "launcher/src"
BRIDGE_DIR = ROOT / "devices/s90d/device/ha-bridge/src"


class _Bridge:
    @staticmethod
    def read_text():
        return "\n".join(p.read_text() for p in sorted(BRIDGE_DIR.glob("*.cs")))


BRIDGE = _Bridge

try:
    from notifications_android_tv.notifications import Notifications  # noqa: E402
except ImportError:  # pragma: no cover - optional
    Notifications = None


@unittest.skipUnless(Notifications and HARNESS.exists() and shutil.which(DOTNET), "harness or client library missing")
class NoticeProtocolTests(unittest.TestCase):
    def test_home_assistant_client_round_trip(self):
        p = subprocess.Popen([DOTNET, str(HARNESS), "3"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            time.sleep(1.5)
            n = Notifications("127.0.0.1")  # GET /: the integration's connection check
            image = SAMPLE.read_bytes() if SAMPLE.exists() else None
            self.assertTrue(n.send("A person was detected on the Doorbell camera.", title="Doorbell", duration=10,
                                   fontsize="large", position="bottom-right", transparency="50%", image_file=image))
            self.assertTrue(n.send("The laundry is done.", title="Washer", position="top-right", fontsize="small"))
            out, err = p.communicate(timeout=20)
        finally:
            p.kill()
        lines = [json.loads(line) for line in out.splitlines() if line.startswith("{")]
        self.assertEqual(lines[0], {"method": "GET"})
        door, washer, misc = lines[1], lines[2], lines[3]
        self.assertEqual((door["Title"], door["Size"], door["Position"], door["Duration"]), ("Doorbell", "large", "bottom-right", 10))
        if image:
            self.assertEqual(door["image"], len(image))
            self.assertEqual(door["Ext"], "jpg")
            self.assertGreater(door["W"], 0)
            self.assertGreater(door["H"], 0)
        # the library's 1x1 placeholder icon is not a picture
        self.assertEqual((washer["Title"], washer["Size"], washer["Position"], washer["image"]), ("Washer", "small", "top-right", 0))
        self.assertEqual(misc["app_hidden"], "org.tizen.tv-viewer")
        self.assertEqual(misc["app_shown"], "com.samsung.tv.bixby-promotion")
        self.assertEqual(misc["app_bar"], "com.samsung.tv.bixby-promotion")
        self.assertEqual(misc["game"], ["Automatic", "Always", "Never"])
        # owner settings override what was sent; pictures off drops a picture-only notice
        self.assertEqual(misc["prefs"], ["top-left", "small", 30, 0])
        self.assertEqual(misc["picture_only_dropped"], True)
        self.assertEqual(misc["quiet"], [True, False, True, False, False])


class NoticeSourceTests(unittest.TestCase):
    """Static guarantees (always run)."""

    def test_banner_never_takes_focus_or_the_picture(self):
        src = (SRC / "Notice.cs").read_text()
        self.assertIn("nw.SetAcceptFocus(false);", src)
        self.assertIn("nw.Type = WindowType.Notification;", src)
        for banned in ("VideoCanvasView", "connect_source", "Window.Default.Show", "ShowBackdrop"):
            self.assertNotIn(banned, src)
        home = (SRC / "SilentGlass.cs").read_text()
        # started only for a notification: the backdrop is built hidden and stays hidden
        self.assertIn("BuildBackdrop();  // hidden until there is nothing to draw over", home)
        self.assertIn('if (x.TryGet("sg_notice", out string _)) { TakeNotice("launch"); return; }', home)
        self.assertIn('<Compile Include="Notice.cs" />', (SRC / "SilentGlass.csproj").read_text())

    def test_bridge_never_wakes_and_only_listens_to_home_assistant(self):
        src = BRIDGE.read_text()
        handle = src[src.index("static string HandleNotice("):src.index("public static Dictionary<string, byte[]> ParseMultipart(")]
        self.assertIn('!ScreenOn ? "the TV is off"', handle)
        self.assertIn('IsAmbient(fg) ? "Ambient is on"', handle)
        # a test from the phone page skips only the owner's own switch and quiet hours
        self.assertIn('!test && !NoticesOn', handle)
        self.assertIn('!test && InQuietHours(', handle)
        self.assertNotIn("PressPower", handle)
        # starting SilentGlass only over the TV viewer (it would push another app to the background)
        self.assertIn('if (onScreen != "org.tizen.tv-viewer" && fg != Launcher)', handle)
        self.assertIn('if (!NoticeAllowed(peer)) { Log("notifications: refused " + peer); return; }', src)
        self.assertIn('conf.GetValueOrDefault("host", "") + "," + conf.GetValueOrDefault("notify_from", "")', src)
        self.assertIn("NoticeMaxBody = 12 << 20", src)


if __name__ == "__main__":
    unittest.main()
