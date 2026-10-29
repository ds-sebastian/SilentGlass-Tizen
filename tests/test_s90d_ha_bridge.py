"""The Home Assistant bridge: the rules in its source that matter on the TV, and the installer that carries it there.
Its behaviour off the TV: tests/test_s90d_setup.py (the phone page) and tests/test_s90d_notice.py (notifications)."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/ha-bridge"
SRC = "\n".join(p.read_text() for p in sorted((DIR / "src").glob("*.cs")))


def member(start, end):
    return SRC[SRC.index(start):SRC.index(end, SRC.index(start))]


class HaBridgeTests(unittest.TestCase):
    def test_commands_are_a_fixed_allowlist(self):
        handler = member("static void OnMessage(", "static void SetOption(")
        cases = {c for line in re.findall(r"case ([^:]+?)(?: when |:)", handler) for c in re.findall(r'"([a-z_0-9/]+)"', line)}
        self.assertEqual(cases, {"input/set", "home/press", "ambient/press", "resync/press", "thread_switch/set", "volume/set", "mute/set",
                                 "brightness/set", "contrast/set", "power/set", "picture_mode/set", "sound_mode/set", "sleep_timer/set",
                                 "sound_output/set", "game_mode/set", "notifications/set"})
        self.assertIn('Regex.Match(cmd, "^hdmi([1-4])_name/set$")', handler)  # the only other command: renaming a port
        self.assertNotIn("/bin/sh", SRC)  # no shell: launch_app, dbus-send and ot-ctl run directly
        # inputs only by exact name from the TV's list; options only from the TV's own lists
        self.assertIn("Inputs().Where(i => i.Name == payload)", handler)
        self.assertIn("opts.Where(x => x.name == name)", SRC)
        # power: a virtual Power key, pressed only when the TV is not already in the requested state
        self.assertRegex(handler, r'if \(\(payload == "ON"\) != ScreenOn\) \{ PressPower\(\);')

    def test_power_key_is_a_uinput_device_with_only_the_power_key(self):
        self.assertIn('open("/dev/uinput", 1 | 2048)', SRC)
        self.assertEqual(SRC.count("ioctl(fd, UiSetKeyBit,"), 1)  # it can press nothing but Power
        self.assertIn("const int KeyPower = 116;", SRC)

    def test_names_go_to_the_default_slot_and_resync_keeps_them(self):
        # index 0 is a separate, unused name slot: writes there never show up in the source list
        self.assertIn("const int Mbr = -2;", SRC)
        self.assertNotRegex(SRC, r"set_edit_name\([^)]*, 0\)")
        resync = member("static void ResyncNames()", "// ---- settings")
        # a new detected name makes the TV reset the port's own name: put it back
        self.assertRegex(resync, r"if \(ok && i\.Edit\.Length > 0\) kept = set_edit_name\(i\.Type, i\.Edit, Mbr\);")
        # never clears a detected name while the TV (and its devices) are in standby
        self.assertIn('on ? "" : i.Detected', resync)
        self.assertNotRegex(SRC, r"\"input_\"|input_\d")  # input names come from the TV, not the config

    def test_source_list_cache_is_kept_current(self):
        # without a GLib loop the library never sees the change signals and renames stay invisible to the bridge
        self.assertIn("register_sourcelist_changed_callback(sourceListCallback, IntPtr.Zero);", SRC)
        self.assertIn("g_main_loop_run(g_main_loop_new(IntPtr.Zero, false));", SRC)

    def test_availability_uses_a_retained_last_will(self):
        self.assertIn('Base + "/availability", "offline"', SRC)
        self.assertIn("0x02 | 0x04 | 0x20", SRC)  # clean session, will flag, will retain
        self.assertNotIn("Timeout.Infinite", member("public Mqtt(", "static void Str("))  # a silent broker is noticed

    def test_notifications_never_wake_the_tv_and_come_only_from_home_assistant(self):
        handle = member("static string HandleNotice(", "public static Dictionary<string, byte[]> ParseMultipart(")
        for why in ('!ScreenOn ? "the TV is off"', 'Shared.IsAmbient(fg) ? "Ambient is on"', "!test && !NoticesOn", "!test && InQuietHours("):
            self.assertIn(why, handle)
        self.assertNotIn("PressPower", handle)
        # SilentGlass is started only over the TV viewer (it would push another app to the background)
        self.assertIn("if (onScreen != Shared.Viewer && fg != Shared.Self)", handle)
        self.assertIn("if (!NoticeAllowed(peer))", SRC)
        self.assertIn('conf.GetValueOrDefault("host", "") + "," + conf.GetValueOrDefault("notify_from", "")', SRC)

    def test_bridge_unit_and_example_ship_no_credentials(self):
        unit = (DIR / "silentglass-ha.service").read_text()
        self.assertNotIn("ConditionPathExists", unit)  # starts unconfigured: the phone setup page writes the config
        self.assertNotIn("User=", unit)  # owner session, never root
        example = (DIR / "silentglass-ha.conf.example").read_text()
        self.assertIn("password=change-me", example)
        self.assertFalse(re.search(r"10\.3\.26\.", (ROOT / "devices/s90d/device/install.sh").read_text() + example))

    def test_thread_switch_is_the_file_the_path_unit_watches(self):
        watched = (ROOT / "devices/s90d/device/thread-border-router/quiet-otbr-switch.path").read_text()
        self.assertIn("bixby-promotion/data/thread-switch", watched)
        self.assertIn('ThreadSwitch = "thread-switch"', (ROOT / "launcher/src/Shared.cs").read_text())


if __name__ == "__main__":
    unittest.main()
