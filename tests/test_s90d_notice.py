"""Notifications on the TV: Home Assistant's own client (notifications-android-tv 0.1.5, the version the nfandroidtv
integration pins) against the bridge's request reader and multipart parser, run off the TV through
tests/notice-harness. Skipped unless the client library is importable:

    uv run --with notifications-android-tv==0.1.5 python -m unittest tests.test_s90d_notice
"""
import json
import os
import shutil
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / ".local/notice-harness"
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet")
# a JPEG as far as the bridge reads one: SOI, a padding segment, SOF0 with the size (896 x 672), EOI
JPEG = b"\xff\xd8\xff\xe0\x04\x02" + bytes(1024) + b"\xff\xc0\x00\x11\x08\x02\xa0\x03\x80\x03" + bytes(9) + b"\xff\xd9"

try:
    from notifications_android_tv.notifications import Notifications  # noqa: E402
except ImportError:  # pragma: no cover - optional
    Notifications = None


@unittest.skipUnless(Notifications and DOTNET, "needs the .NET 6 SDK and notifications-android-tv")
class NoticeProtocolTests(unittest.TestCase):
    def test_home_assistant_client_round_trip(self):
        b = subprocess.run([DOTNET, "build", str(ROOT / "tests/notice-harness/NoticeHarness.csproj"), "-c", "Release", "-o", str(OUT),
                            f"-p:BaseIntermediateOutputPath={ROOT / '.local/obj/NoticeHarness'}/"], capture_output=True, text=True, timeout=600)
        self.assertEqual(b.returncode, 0, b.stdout[-2000:])
        p = subprocess.Popen([DOTNET, str(OUT / "NoticeHarness.dll"), "3"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            self.assertEqual(p.stderr.readline().strip(), "listening")  # the harness is ready
            n = Notifications("127.0.0.1")  # GET /: the integration's connection check
            self.assertTrue(n.send("A person was detected on the Doorbell camera.", title="Doorbell", duration=10,
                                   fontsize="large", position="bottom-right", transparency="50%", image_file=JPEG))
            self.assertTrue(n.send("The laundry is done.", title="Washer", position="top-right", fontsize="small"))
            out, err = p.communicate(timeout=20)
        finally:
            p.kill()
        lines = [json.loads(line) for line in out.splitlines() if line.startswith("{")]
        self.assertEqual(lines[0], {"method": "GET"})
        door, washer, misc = lines[1], lines[2], lines[3]
        self.assertEqual((door["Title"], door["Size"], door["Position"], door["Duration"]), ("Doorbell", "large", "bottom-right", 10))
        self.assertEqual((door["image"], door["Ext"], door["W"], door["H"]), (len(JPEG), "jpg", 896, 672))
        # the library's 1x1 placeholder icon is not a picture
        self.assertEqual((washer["Title"], washer["Size"], washer["Position"], washer["image"]), ("Washer", "small", "top-right", 0))
        self.assertEqual(misc["app_hidden"], "org.tizen.tv-viewer")
        self.assertEqual(misc["app_shown"], "com.samsung.tv.bixby-promotion")
        self.assertEqual(misc["app_bar"], "com.samsung.tv.bixby-promotion")
        self.assertEqual(misc["game"], ["Automatic", "Always", "Never"])
        self.assertEqual(misc["names"], ["Shield (HDMI 1)", "HDMI 2", "Shield (HDMI 3)", "HDMI 4"])  # one name, two ports
        # owner settings override what was sent; pictures off drops a picture-only notice
        self.assertEqual(misc["prefs"], ["top-left", "small", 30, 0])
        self.assertEqual(misc["picture_only_dropped"], True)
        self.assertEqual(misc["slugs"], ["living_room_tv", "den_tv_2", "t_l_viseur", "samsung_tv"])
        self.assertEqual(misc["quiet"], [True, False, True, False, False])


if __name__ == "__main__":
    unittest.main()
