import base64
import gzip
import hashlib
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/ha-bridge"
SRC = "\n".join(p.read_text() for p in sorted((DIR / "src").glob("*.cs")))


class HaBridgeTests(unittest.TestCase):
    def test_commands_are_a_fixed_allowlist(self):
        handler = SRC.split("static void OnMessage(")[1].split("static void LaunchApp(")[0]
        cases = {c for line in re.findall(r"case ([^:]+?)(?: when |:)", handler) for c in re.findall(r'"([a-z_0-9/]+)"', line)}
        self.assertEqual(cases, {"input/set", "home/press", "ambient/press", "resync/press", "thread_switch/set", "volume/set", "mute/set",
                                 "brightness/set", "contrast/set", "power/set", "picture_mode/set", "sound_mode/set", "sleep_timer/set",
                                 "sound_output/set", "game_mode/set", "notifications/set"})
        self.assertIn('Regex.Match(cmd, "^hdmi([1-4])_name/set$")', handler)  # the only other command: renaming a port
        self.assertNotIn("/bin/sh", SRC)  # no shell: launch_app, dbus-send and ot-ctl run directly
        # input requests only for an input the TV lists, by exact name; options only from the TV's own lists
        self.assertIn("Inputs().Where(i => i.Name == payload)", handler)
        self.assertIn("opts.Where(x => x.name == name)", SRC)
        # power: a virtual Power key, pressed only when the TV is not already in the requested state
        self.assertIn('if ((payload == "ON") != ScreenOn) { PressPower();', handler)

    def test_names_are_written_to_the_default_slot(self):
        # index 0 is a separate, unused name slot: writes there never show up in the source list
        self.assertIn("const int Mbr = -2;", SRC)
        self.assertIn("set_edit_name(12 + int.Parse(m.Groups[1].Value), name, Mbr)", SRC)
        self.assertNotRegex(SRC, r"set_edit_name\([^)]*, 0\)")

    def test_source_list_cache_is_kept_current(self):
        # without a GLib loop the library never sees the change signals and renames stay invisible to the bridge
        self.assertIn("register_sourcelist_changed_callback(sourceListCallback, IntPtr.Zero);", SRC)
        self.assertIn("g_main_loop_run(g_main_loop_new(IntPtr.Zero, false));", SRC)

    def test_power_key_is_a_uinput_device_with_only_the_power_key(self):
        self.assertIn('open("/dev/uinput", 1 | 2048)', SRC)
        self.assertEqual(SRC.count("ioctl(fd, UiSetKeyBit,"), 1)  # it can press nothing but Power
        self.assertIn("const int KeyPower = 116;", SRC)
        self.assertFalse((DIR / "ha-power-automation.yaml").exists())  # no Home Assistant automation needed

    def test_input_names_come_from_the_tv(self):
        self.assertNotRegex(SRC, r"\"input_\"|input_\d")  # no config labels
        self.assertIn('i.Name = i.Edit.Length > 0 ? i.Edit : i.Detected.Length > 0 ? i.Detected : i.Port;', SRC)
        # changing a detected name makes the TV reset the port's own name: resync must restore it
        self.assertIn("if (ok && i.Edit.Length > 0) kept = set_edit_name(i.Type, i.Edit, Mbr);", SRC)
        # resync never clears a detected name while the TV (and its devices) are in standby
        self.assertIn('string want = live.TryGetValue(i.Type, out var n) ? n : on ? "" : i.Detected;', SRC)

    def test_thread_switch_works_like_the_info_switch(self):
        # the marker gates the units; the trigger file is what quiet-otbr-switch.path watches
        self.assertIn('Data + "/thread-off"', SRC)
        self.assertIn('Data + "/thread-switch"', SRC)
        watched = (ROOT / "devices/s90d/device/thread-border-router/quiet-otbr-switch.path").read_text()
        self.assertIn("bixby-promotion/data/thread-switch", watched)

    def test_launcher_accepts_source_requests_only_for_listed_inputs(self):
        home = (ROOT / "launcher/src/SilentGlass.cs").read_text()
        self.assertIn('if (x.TryGet("sg_source", out string src) && int.TryParse(src, out int t) && inputs.Exists(i => i.Type == t)) { SelectSource(t); return; }', home)

    def test_availability_uses_a_retained_last_will(self):
        self.assertIn('Base + "/availability", "offline"', SRC)
        self.assertIn("0x02 | 0x04 | 0x20", SRC)  # clean session, will flag, will retain

    def test_installer_ships_the_built_bridge_and_no_credentials(self):
        inst = (DIR / "ha-bridge-install.sh").read_text()
        for dest in ("$Q/silentglass-ha.runtimeconfig.json", "$UD/$N"):
            self.assertIn(dest + '.new" <<', inst)
        # the DLL travels gzipped: the TV runner takes at most 64 KiB per command
        self.assertIn('base64 -d <<\'B64_EOF\' | gzip -dc > "$Q/silentglass-ha.dll.new"', inst)
        self.assertLess(len(inst.encode()), 64 * 1024)
        self.assertIn("N=silentglass-ha.service", inst)
        blocks = re.findall(r"<<'B64_EOF'[^\n]*\n(.*?)\nB64_EOF\n\[ \"\$\(sha256sum [^)]*\)\" = ([0-9a-f]{64}) \]", inst, re.S)
        self.assertEqual(len(blocks), 4)
        for b64, sha in blocks:
            data = base64.b64decode(b64)
            if data[:2] == b"\x1f\x8b":
                data = gzip.decompress(data)
            self.assertEqual(hashlib.sha256(data).hexdigest(), sha)
        unit = (DIR / "silentglass-ha.service").read_text()
        self.assertNotIn("ConditionPathExists", unit)  # starts unconfigured: the phone setup page writes the config
        self.assertNotIn("User=", unit)  # owner session, never root
        example = (DIR / "silentglass-ha.conf.example").read_text()
        self.assertIn("password=change-me", example)
        self.assertNotIn("input_", example)
        self.assertFalse(re.search(r"10\.3\.26\.", inst + example))


if __name__ == "__main__":
    unittest.main()
