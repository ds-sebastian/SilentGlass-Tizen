"""S90D Thread border router recipe: the installer ships exactly the published units, and the inverse undoes them."""
import base64
import hashlib
import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/thread-border-router"
UNITS = sorted(p.name for p in DIR.glob("quiet-otbr-*") if p.suffix in (".service", ".path"))
DATA = "/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data"


def embedded_units(install):
    """unit name -> (decoded bytes, sha256 the script checks) for every heredoc in the installer."""
    found = {}
    pattern = r'base64 -d > "\$UD/([\w.-]+)\.new" <<\'B64_EOF\'\n(.*?)\nB64_EOF\n\[ "\$\(sha256sum "\$UD/\1\.new" \| cut -d" " -f1\)" = ([0-9a-f]{64}) \]'
    for name, b64, sha in re.findall(pattern, install, re.S):
        found[name] = (base64.b64decode(b64), sha)
    return found


class ThreadBorderRouterRecipe(unittest.TestCase):
    def test_scripts_parse(self):
        for name in ("otbr-install.sh", "otbr-restore.sh", "otbr-switch-update.sh", "otbr-switch-restore.sh", "otbr-join.sh",
                     "otbr-standby-update.sh", "otbr-standby-restore.sh"):
            subprocess.run(["sh", "-n", str(DIR / name)], check=True, capture_output=True)

    def test_installer_embeds_the_published_units(self):
        self.assertTrue(UNITS)
        embedded = embedded_units((DIR / "otbr-install.sh").read_text())
        self.assertEqual(sorted(embedded), UNITS)
        for name, (data, sha) in embedded.items():
            self.assertEqual(data, (DIR / name).read_bytes(), name)
            self.assertEqual(hashlib.sha256(data).hexdigest(), sha, name)

    def test_restore_removes_every_installed_unit(self):
        install = (DIR / "otbr-install.sh").read_text()
        restore = (DIR / "otbr-restore.sh").read_text()
        for name in UNITS:
            if name != "quiet-otbr-switch.service":  # started only by its path unit
                self.assertIn(f'"$UD/default.target.wants/{name}"', install)
            self.assertIn(name, restore)
        self.assertIn('rm -f "$UD/default.target.wants/$n" "$UD/$n"', restore)

    def test_switch_update_ships_the_same_units_and_restore_ships_the_merged_ones(self):
        update = embedded_units((DIR / "otbr-switch-update.sh").read_text())
        self.assertEqual(sorted(update), [u for u in UNITS if u != "quiet-otbr-usbpower.service"])
        for name, (data, sha) in update.items():
            self.assertEqual(data, (DIR / name).read_bytes(), name)
        back = embedded_units((DIR / "otbr-switch-restore.sh").read_text())
        self.assertEqual(sorted(back), ["quiet-otbr-agent.service", "quiet-otbr-cpcd.service", "quiet-otbr-forward.service"])
        for name, (data, sha) in back.items():
            self.assertEqual(hashlib.sha256(data).hexdigest(), sha, name)
            self.assertNotIn(b"thread-off", data)  # the always-on units

    def test_info_switch_gates_every_daemon_and_survives_reboots(self):
        # boot and manual starts both skip while the marker exists; conditions never trigger Restart=
        for name in ("quiet-otbr-cpcd.service", "quiet-otbr-agent.service", "quiet-otbr-forward.service", "quiet-otbr-usbpower.service"):
            self.assertIn(f"ConditionPathExists=!{DATA}/thread-off", (DIR / name).read_text(), name)
        path = (DIR / "quiet-otbr-switch.path").read_text()
        self.assertIn(f"PathChanged={DATA}/thread-switch", path)
        self.assertIn("WantedBy=default.target", path)
        switch = (DIR / "quiet-otbr-switch.service").read_text()
        self.assertIn("Type=oneshot", switch)
        # off: agent stops before the radio daemon (it hangs once CPC is gone)
        self.assertIn("stop quiet-otbr-forward.service quiet-otbr-agent.service quiet-otbr-cpcd.service quiet-otbr-usbpower.service", switch)
        self.assertIn("start --no-block quiet-otbr-usbpower.service", switch)

    def test_standby_blocks_iot_hub_control_and_restore_unblocks_it(self):
        # Samsung's iot-hub-control switches the radio module off at standby while the SmartThings hub is off
        for script in ("otbr-install.sh", "otbr-standby-update.sh"):
            text = (DIR / script).read_text()
            self.assertIn("SET app_disable='true' WHERE app_id='com.samsung.tv.iot-hub-control'", text, script)
            self.assertIn('touch "$B/ihc-disabled-by-quiet-otbr"', text, script)
        for script in ("otbr-restore.sh", "otbr-standby-restore.sh"):
            text = (DIR / script).read_text()
            self.assertIn("SET app_disable='false' WHERE app_id='com.samsung.tv.iot-hub-control'", text, script)
            self.assertIn('if [ -e "$B/ihc-disabled-by-quiet-otbr" ]', text, script)  # only undo what we did

    def test_usb_power_keeper_ships_the_built_keeper(self):
        # without it storaged powers the radio's USB port down ~20 min into standby
        unit = (DIR / "quiet-otbr-usbpower.service").read_text()
        self.assertIn("/usr/bin/dotnet /opt/usr/share/quiet-otbr/quiet-otbr-usbpower.dll on", unit)
        self.assertIn("Restart=always", unit)
        src = (DIR / "usb-power-keeper/Program.cs").read_text()
        self.assertIn('"UsbDeviceChangePortPower"', src)
        self.assertIn("dbus_bus_get_private(1", src)  # system bus
        for script in ("otbr-install.sh", "otbr-standby-update.sh"):
            text = (DIR / script).read_text()
            self.assertIn('"$Q/quiet-otbr-usbpower.dll.new"', text, script)
            self.assertIn('"$UD/default.target.wants/quiet-otbr-usbpower.service"', text, script)
        install = (DIR / "otbr-install.sh").read_text()
        self.assertIn('"$UD/default.target.wants/quiet-otbr-switch.path"', install)
        self.assertNotIn('"$UD/default.target.wants/quiet-otbr-switch.service"', install)  # only the path unit starts it
        restore = (DIR / "otbr-restore.sh").read_text()
        self.assertIn("thread-off", restore)

    def test_agent_backbone_follows_the_default_route(self):
        agent = (DIR / "quiet-otbr-agent.service").read_text()
        self.assertNotIn("-B eth0", agent)  # Wi-Fi TVs route through wlan0
        self.assertIn('ip route show default', agent)
        self.assertIn('-B "$$b"', agent)

    def test_join_never_echoes_the_dataset_and_refuses_to_replace_a_network(self):
        join = (DIR / "otbr-join.sh").read_text()
        self.assertNotRegex(join, r'echo[^\n]*\$\{?DATASET')
        self.assertIn("FORCE=1 replaces the saved network", join)
        self.assertIn("dataset init tlvs", join)
        self.assertIn("dataset init new", join)
        pi = (ROOT / "devices/s90d/pi/thread_dataset.py").read_text()
        self.assertIn("0o600", pi)
        self.assertNotRegex(pi, r"print\([^)]*tlv")

    def test_units_wait_inside_execstart_and_never_flash(self):
        for name in UNITS:
            if not name.endswith(".service") or name == "quiet-otbr-switch.service":
                continue
            text = (DIR / name).read_text()
            self.assertIn("Type=simple", text, name)
            self.assertNotRegex(text, r"(?m)^ExecStartPre=", name)
            self.assertNotIn("ncp-updater", text, name)

    def test_drift_check_expects_the_units(self):
        import json
        expected = json.loads((ROOT / "devices/s90d/pi/s90d_expected.json").read_text())
        for name in UNITS:
            if name != "quiet-otbr-switch.service":  # started only by its path unit
                self.assertIn(name, expected["user_units"])


if __name__ == "__main__":
    unittest.main()
