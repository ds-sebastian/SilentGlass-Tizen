"""The bridge's phone setup page, run off the TV: `silentglass-ha.dll --no-tv` with a temporary data directory and
config, against a fake MQTT broker. Covers the code gate, the 10-minute page lifecycle files, validation, saving
(the password is never echoed and blank keeps it), the reconnect with the new settings, and wrong credentials."""
import json, os, shutil, socket, subprocess, tempfile, threading, time, unittest, urllib.request, urllib.error
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DLL = Path(os.environ.get("SG_BRIDGE_DLL", ROOT / ".local/build/ha-bridge/silentglass-ha.dll"))  # tools/build.py bridge
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet") or "dotnet"


class FakeBroker:
    """Accepts MQTT CONNECT; refuses with code 4 (after a second) when the password is 'wrong'. Records client ids and credentials."""
    def __init__(self):
        self.sock = socket.socket(); self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.sock.bind(("127.0.0.1", 0)); self.sock.listen(8); self.port = self.sock.getsockname()[1]
        self.connects = []; threading.Thread(target=self.serve, daemon=True).start()

    def serve(self):
        while True:
            try: c, _ = self.sock.accept()
            except OSError: return
            threading.Thread(target=self.client, args=(c,), daemon=True).start()

    def client(self, c):
        try:
            head = c.recv(1)
            mult, length = 1, 0
            while True:
                d = c.recv(1)[0]; length += (d & 127) * mult; mult *= 128
                if not d & 128: break
            body = b""
            while len(body) < length: body += c.recv(length - len(body))
            flags = body[7]; pos = 10
            def s():
                nonlocal pos
                n = int.from_bytes(body[pos:pos + 2], "big"); v = body[pos + 2:pos + 2 + n].decode(); pos += 2 + n; return v
            cid = s()
            if flags & 4: s(); s()
            user = s() if flags & 128 else None
            pw = s() if flags & 64 else None
            self.connects.append((cid, user, pw))
            if pw == "wrong": time.sleep(1)  # a broker on the network takes a moment to refuse
            c.sendall(bytes([0x20, 2, 0, 4 if pw == "wrong" else 0]))
            while c.recv(64): pass
        except Exception: pass
        finally: c.close()


def free_port():
    s = socket.socket(); s.bind(("127.0.0.1", 0)); p = s.getsockname()[1]; s.close(); return p


@unittest.skipUnless(DLL.exists() and shutil.which(DOTNET), "build the bridge first: python3 tools/build.py bridge")
class SetupPageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); t = Path(self.tmp.name)
        self.data = t / "data"; self.data.mkdir(); self.conf = t / "silentglass-ha.conf"
        self.broker = FakeBroker(); self.port = free_port()
        env = dict(os.environ, SG_DATA=str(self.data), SG_CONF=str(self.conf), SG_SETUP_PORT=str(self.port), DOTNET_CLI_TELEMETRY_OPTOUT="1")
        self.proc = subprocess.Popen([DOTNET, str(DLL), "--no-tv"], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)

    def tearDown(self):
        self.proc.kill(); self.proc.wait(); self.broker.sock.close(); self.tmp.cleanup()

    def wait_for(self, check, seconds=15):
        end = time.time() + seconds
        while time.time() < end:
            v = check()
            if v: return v
            time.sleep(0.1)
        self.fail("timed out")

    def status(self):
        try: return json.loads((self.data / "setup.json").read_text())
        except Exception: return None

    def open_page(self):
        (self.data / "setup-request").write_text("now\n")
        s = self.wait_for(lambda: (st := self.status()) and st["state"] == "waiting" and st)
        self.assertRegex(s["code"], r"^\d{6}$"); self.assertTrue(s["url"].endswith(":%d/" % self.port))
        return s["code"]

    def api(self, path, body=None, code=None):
        req = urllib.request.Request("http://127.0.0.1:%d%s" % (self.port, path), data=None if body is None else json.dumps(body).encode(),
                                     headers={"X-Code": code or "", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=40) as r: return r.status, r.read()
        except urllib.error.HTTPError as e: return e.code, e.read()
        except urllib.error.URLError: return 410, b""  # the page is closed: nothing listens any more

    def test_unconfigured_then_setup_and_save(self):
        self.wait_for(lambda: (self.data / "bridge-status.txt").exists())
        self.assertIn("mqtt=unconfigured", (self.data / "bridge-status.txt").read_text())
        code = self.open_page()
        st, page = self.api("/")
        self.assertEqual(st, 200); self.assertIn(b"Enter the code shown on the TV", page)
        self.assertEqual(self.api("/api/state", code="000000" if code != "000000" else "111111")[0], 403)
        st, body = self.api("/api/state", code=code); state = json.loads(body)
        self.assertEqual((st, state["mqtt"], state["has_password"], state["update"]), (200, "unconfigured", False, 10))
        self.assertEqual(self.status()["state"], "phone")
        # validation
        for bad in ({"host": "bad host!"}, {"host": "h", "port": "70000"}, {"host": "h", "name": "a\nb"}, {"host": "h", "thread": "tlv", "tlv": "zz"},
                    {"host": "h", "lat": "100"}, {"host": "h", "notify_from": "x;rm"}, {"host": "h", "update": "1"}, {"host": "h", "update": "3;x"},
                    {"host": "h", "notify_position": "middle"}, {"host": "h", "notify_duration": "7"},
                    {"host": "h", "notify_quiet_from": "25:00", "notify_quiet_to": "07:00"}, {"host": "h", "notify_quiet_from": "22:00"}):
            ok = json.loads(self.api("/api/save", bad, code)[1])["ok"]
            self.assertFalse(ok, bad)
        self.assertFalse(self.conf.exists())
        # test button: wrong password, then right
        r = json.loads(self.api("/api/test", {"host": "127.0.0.1", "port": self.broker.port, "user": "tv", "password": "wrong"}, code)[1])
        self.assertEqual((r["ok"], "wrong username or password" in r["message"]), (False, True))
        r = json.loads(self.api("/api/test", {"host": "127.0.0.1", "port": self.broker.port, "user": "tv", "password": "secret"}, code)[1])
        self.assertTrue(r["ok"], r)
        # save: conf written owner-only, bridge connects with it
        r = json.loads(self.api("/api/save", {"host": "127.0.0.1", "port": str(self.broker.port), "user": "tv", "password": "secret", "name": "Den TV",
                                              "notifications": False, "notify_from": "192.0.2.5", "lat": "40.7", "lon": "-74.0", "thread": "keep", "update": "30",
                                              "notify_position": "top-right", "notify_size": "medium", "notify_duration": "10", "notify_images": "0",
                                              "notify_quiet_from": "22:30", "notify_quiet_to": "07:00"}, code)[1])
        self.assertTrue(r["ok"], r); self.assertIn("Connected", r["message"])
        self.assertEqual(oct(self.conf.stat().st_mode & 0o777), "0o600")
        conf = self.conf.read_text()
        for line in ("host=127.0.0.1", "user=tv", "password=secret", "name=Den TV", "notify_from=192.0.2.5", "update=30",
                     "notify_position=top-right", "notify_size=medium", "notify_duration=10", "notify_images=0", "notify_quiet=22:30-07:00"): self.assertIn(line, conf)
        self.assertTrue((self.data / "notify-off").exists()); self.assertIn("lat=40.7", (self.data / "location.txt").read_text())
        self.assertIn(("silentglass-s90d", "tv", "secret"), self.broker.connects)
        self.assertEqual(self.status()["state"], "saved")
        # the password is never sent back; blank keeps it
        state = json.loads(self.api("/api/state", code=code)[1])
        self.assertNotIn("secret", json.dumps(state)); self.assertTrue(state["has_password"]); self.assertEqual((state["mqtt"], state["update"]), ("connected", 30))
        self.assertEqual((state["notify_position"], state["notify_size"], state["notify_duration"], state["notify_images"], state["notify_quiet"]),
                         ("top-right", "medium", "10", False, "22:30-07:00"))
        # the test button checks the page's values and needs the TV
        self.assertFalse(json.loads(self.api("/api/notify-test", {"notify_size": "huge"}, code)[1])["ok"])
        self.assertEqual(json.loads(self.api("/api/notify-test", {}, code)[1])["message"], "Not available here.")
        r = json.loads(self.api("/api/save", {"host": "127.0.0.1", "port": str(self.broker.port), "user": "tv", "password": "", "name": "Den TV", "notifications": True, "thread": "keep"}, code)[1])
        self.assertTrue(r["ok"], r); self.assertIn("password=secret", self.conf.read_text()); self.assertFalse((self.data / "notify-off").exists())
        # closing on the TV ends the page
        (self.data / "setup-close").write_text("x")
        self.wait_for(lambda: (st := self.status()) and st["state"] == "closed")
        self.assertEqual(self.api("/api/state", code=code)[0], 410)

    def test_home_screen_settings_go_to_silentglass_prefs(self):
        # SilentGlass's own prefs file: the page changes its keys and keeps the rest
        (self.data / "silentglass-prefs.txt").write_text("watch=0\nplay=0\nhideapps=0\ndim=1\nwall=auto\nlat=40.7\nlon=-74.0\n")
        code = self.open_page()
        state = json.loads(self.api("/api/state", code=code)[1])
        self.assertEqual((state["watch"], state["play"], state["hideapps"], state["dim"], state["wall"]), ("0", "0", False, "5", "auto"))
        self.assertEqual((state["clock"], state["inputalerts"]), ("12", True))
        self.assertEqual([i["type"] for i in state["inputs"]], [13, 14, 15, 16])
        self.assertEqual(state["close"], "20")
        for bad in ({"watch": "15", "play": "15"}, {"watch": "12", "play": "13"}, {"watch": "15", "play": "13", "wall": "dusk"},
                    {"watch": "15", "play": "13", "close": "7"}, {"watch": "15", "play": "13", "dim": "1"},
                    {"watch": "15", "play": "13", "clock": "13"}):
            self.assertFalse(json.loads(self.api("/api/save", dict(bad, thread="keep"), code)[1])["ok"], bad)
        r = json.loads(self.api("/api/save", {"watch": "15", "play": "13", "hideapps": "1", "dim": "0", "wall": "night", "close": "0", "clock": "24", "inputalerts": "0", "thread": "keep", "notifications": True}, code)[1])
        self.assertTrue(r["ok"], r)
        prefs = dict(l.split("=", 1) for l in (self.data / "silentglass-prefs.txt").read_text().split())
        self.assertEqual(prefs, {"watch": "15", "play": "13", "hideapps": "1", "dim": "0", "wall": "night", "lat": "40.7", "lon": "-74.0", "close": "0", "clock": "24", "inputalerts": "0"})
        state = json.loads(self.api("/api/state", code=code)[1])
        self.assertEqual((state["watch"], state["hideapps"], state["dim"], state["wall"], state["clock"], state["inputalerts"]), ("15", True, "0", "night", "24", False))
        # the page never carries the code in its address: the QR is the address only, the code is typed
        st, page = self.api("/")
        self.assertIn(b"Advanced settings", page); self.assertIn(b"z-index:10", page)
        self.assertNotIn(b"location.search", page)  # the code is never read from the address

    def test_new_settings_report_their_own_outcome(self):
        # connected first; then a wrong password must not be reported with the old connection's "Connected"
        code = self.open_page()
        good = {"host": "127.0.0.1", "port": str(self.broker.port), "user": "tv", "password": "secret", "notifications": True, "thread": "keep"}
        r = json.loads(self.api("/api/save", good, code)[1])
        self.assertTrue(r["ok"], r); self.assertIn("Connected", r["message"])
        r = json.loads(self.api("/api/save", dict(good, password="wrong"), code)[1])
        self.assertFalse(r["ok"], r); self.assertIn("wrong username or password", r["message"])
        self.assertIn("mqtt=error", (self.data / "bridge-status.txt").read_text())
        self.assertEqual(self.status()["state"], "error")

    def test_wrong_codes_close_the_page(self):
        code = self.open_page(); wrong = "%06d" % ((int(code) + 1) % 1000000)
        for _ in range(7): self.assertEqual(self.api("/api/state", code=wrong)[0], 403)
        self.assertEqual(self.api("/api/state", code=wrong)[0], 410)
        self.assertEqual(self.api("/api/state", code=code)[0], 410)
        self.assertEqual(self.status()["state"], "closed")


if __name__ == "__main__":
    unittest.main()
