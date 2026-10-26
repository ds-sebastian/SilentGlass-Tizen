import base64
import hashlib
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/ha-bridge"
SRC = (DIR / "src/Program.cs").read_text()


class HaBridgeTests(unittest.TestCase):
    def test_commands_are_a_fixed_allowlist(self):
        handler = SRC.split("static void OnMessage(")[1].split("static void LaunchApp(")[0]
        topics = set(re.findall(r'Base \+ "(/[a-z_/]+)"', handler))
        self.assertEqual(topics, {"/input/set", "/home/press", "/ambient/press", "/thread_switch/set"})
        self.assertNotIn("/bin/sh", SRC)  # no shell: launch_app and ot-ctl run directly
        # input requests only for an input the TV lists, by exact name
        self.assertIn("Inputs().Where(i => i.name == payload)", handler)

    def test_thread_switch_works_like_the_info_switch(self):
        # the marker gates the units; the trigger file is what quiet-otbr-switch.path watches
        self.assertIn('Data + "/thread-off"', SRC)
        self.assertIn('Data + "/thread-switch"', SRC)
        watched = (ROOT / "devices/s90d/device/thread-border-router/quiet-otbr-switch.path").read_text()
        self.assertIn("bixby-promotion/data/thread-switch", watched)

    def test_launcher_accepts_source_requests_only_for_listed_inputs(self):
        home = (ROOT / "launcher/src/Home.cs").read_text()
        self.assertIn('TryGet("quiet_source",out string req)&&int.TryParse(req,out int src)&&inputs.Exists(i=>i.Type==src)', home)

    def test_availability_uses_a_retained_last_will(self):
        self.assertIn('Base + "/availability", "offline"', SRC)
        self.assertIn("0x02 | 0x04 | 0x20", SRC)  # clean session, will flag, will retain

    def test_installer_ships_the_built_bridge_and_no_credentials(self):
        inst = (DIR / "ha-bridge-install.sh").read_text()
        for dest in ("$Q/silentglass-ha.dll", "$Q/silentglass-ha.runtimeconfig.json", "$UD/$N"):
            self.assertIn(dest + '.new" <<', inst)
        self.assertIn("N=silentglass-ha.service", inst)
        blocks = re.findall(r"<<'B64_EOF'\n(.*?)\nB64_EOF\n\[ \"\$\(sha256sum [^)]*\)\" = ([0-9a-f]{64}) \]", inst, re.S)
        self.assertEqual(len(blocks), 4)
        for b64, sha in blocks:
            self.assertEqual(hashlib.sha256(base64.b64decode(b64)).hexdigest(), sha)
        unit = (DIR / "silentglass-ha.service").read_text()
        self.assertIn("ConditionPathExists=/opt/usr/home/owner/.config/silentglass-ha.conf", unit)
        self.assertNotIn("User=", unit)  # owner session, never root
        example = (DIR / "silentglass-ha.conf.example").read_text()
        self.assertIn("password=change-me", example)
        self.assertFalse(re.search(r"10\.3\.26\.", inst + example))


if __name__ == "__main__":
    unittest.main()
