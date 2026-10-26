"""S90D Thread border router recipe: the installer ships exactly the published units, and the inverse undoes them."""
import base64
import hashlib
import re
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/thread-border-router"
UNITS = sorted(p.name for p in DIR.glob("*.service"))


def embedded_units(install):
    """unit name -> (decoded bytes, sha256 the script checks) for every heredoc in the installer."""
    found = {}
    pattern = r'base64 -d > "\$UD/([\w.-]+)\.new" <<\'B64_EOF\'\n(.*?)\nB64_EOF\n\[ "\$\(sha256sum "\$UD/\1\.new" \| cut -d" " -f1\)" = ([0-9a-f]{64}) \]'
    for name, b64, sha in re.findall(pattern, install, re.S):
        found[name] = (base64.b64decode(b64), sha)
    return found


class ThreadBorderRouterRecipe(unittest.TestCase):
    def test_scripts_parse(self):
        for name in ("otbr-install.sh", "otbr-restore.sh"):
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
            self.assertIn(f'"$UD/default.target.wants/{name}"', install)
            self.assertIn(name, restore)
        self.assertIn('rm -f "$UD/default.target.wants/$n" "$UD/$n"', restore)

    def test_units_wait_inside_execstart_and_never_flash(self):
        for name in UNITS:
            text = (DIR / name).read_text()
            self.assertIn("Type=simple", text, name)
            self.assertNotRegex(text, r"(?m)^ExecStartPre=", name)
            self.assertNotIn("ncp-updater", text, name)

    def test_drift_check_expects_the_units(self):
        import json
        expected = json.loads((ROOT / "devices/s90d/pi/s90d_expected.json").read_text())
        for name in UNITS:
            self.assertIn(name, expected["user_units"])


if __name__ == "__main__":
    unittest.main()
