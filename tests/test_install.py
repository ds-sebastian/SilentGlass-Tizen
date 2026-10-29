"""install.sh and undo.sh, run for real against a fake TV in a temporary folder (SILENTGLASS_TEST_ROOT), with the
Tizen-only commands (chsmack, app_launcher, systemctl, chown, id) stubbed. Covers update, already-current, undo,
a fresh bridge, and the refusals that must leave the TV untouched."""
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEV = ROOT / "devices/s90d/device"
sys.path.insert(0, str(ROOT / "tools"))
import bundle  # noqa: E402

STUBS = {
    "id": "echo 0",
    "chown": "exit 0",
    "app_launcher": 'echo "app_launcher $*" >> "$SILENTGLASS_TEST_ROOT/calls"',
    "chsmack": 'case "$1" in -a) exit 0;; *) echo "$1 access=\\"User::Pkg::owner\\"";; esac',
    "systemctl": 'echo "systemctl $*" >> "$SILENTGLASS_TEST_ROOT/calls"; case "$2" in is-enabled) echo enabled;; is-active) echo active;; esac',
    "sleep": "exit 0",
}


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def tree(root: Path) -> dict:
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob("*")) if p.is_file()}


class InstallUndo(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="sg-install-"))
        self.tv = self.tmp / "tv"
        self.app = self.tv / "opt/usr/apps/com.samsung.tv.bixby-promotion"
        self.q = self.tv / "opt/usr/share/silentglass-ha"
        self.ud = self.tv / "opt/usr/home/owner/.config/systemd/user"
        self.backups = self.tv / "opt/usr/share/quiet-tizen-backups"
        self.stage = self.tv / "home/owner/share/tmp/sdk_tools/quiet-tizen"
        stubs = self.tmp / "bin"; stubs.mkdir()
        for name, body in STUBS.items():
            (stubs / name).write_text("#!/bin/sh\n" + body + "\n"); (stubs / name).chmod(0o755)
        self.env = {**os.environ, "SILENTGLASS_TEST_ROOT": str(self.tv), "PATH": f"{stubs}:{os.environ['PATH']}"}
        # an installed older SilentGlass and bridge, and the owner's bridge settings
        (self.app / "bin").mkdir(parents=True); (self.app / "res/quiet-home").mkdir(parents=True)
        (self.app / "bin/BixbyPromotion.dll").write_bytes(b"MZ old SilentGlass build")
        (self.app / "res/quiet-home/sym-old.svg").write_text("<svg/>")
        self.ud.mkdir(parents=True); self.q.mkdir(parents=True); self.backups.mkdir(parents=True)
        (self.q / "silentglass-ha.dll").write_bytes(b"old bridge"); (self.q / "silentglass-ha.runtimeconfig.json").write_text("{}")
        (self.ud / "silentglass-ha.service").write_text("[Unit]\nDescription=old\n")
        self.conf = self.tv / "opt/usr/home/owner/.config/silentglass-ha.conf"; self.conf.write_text("host=broker\npassword=secret\n")
        # a release bundle with fake builds, and its install.sh
        pkg, br = self.tmp / "package", self.tmp / "bridge"
        (pkg / "quiet-home").mkdir(parents=True); br.mkdir()
        (pkg / "BixbyPromotion.dll").write_bytes(b"MZ new SilentGlass build"); (pkg / "Probe.dll").write_bytes(b"MZ probe")
        (pkg / "quiet-home/sym-new.svg").write_text("<svg new/>"); (pkg / "quiet-home/apps.json").write_text("[]")
        (br / "silentglass-ha.dll").write_bytes(b"new bridge"); (br / "silentglass-ha.runtimeconfig.json").write_text('{"new":1}')
        out = self.tmp / "dist"
        subprocess.run([sys.executable, str(ROOT / "tools/bundle.py"), "--package", str(pkg), "--bridge", str(br),
                        "--out", str(out), "--installer", str(out / "install.sh")], check=True, capture_output=True)
        self.installer, self.bundle = out / "install.sh", next(out.glob("silentglass-*.tar.gz"))
        self.before = {"app": tree(self.app), "q": tree(self.q), "ud": tree(self.ud)}

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def run_sh(self, script: Path, *args, stage=True):
        if stage:
            self.stage.mkdir(parents=True, exist_ok=True); shutil.copyfile(self.bundle, self.stage / "package.tar.gz")
        time.sleep(1.05)  # backup folders are named to the second
        with script.open() as f:
            r = subprocess.run(["sh", "-s", "--", *args], stdin=f, env=self.env, capture_output=True, text=True)
        return r.returncode, r.stdout + r.stderr

    def now(self):
        return {"app": tree(self.app), "q": tree(self.q), "ud": tree(self.ud)}

    def test_update_then_already_current_then_undo(self):
        rc, out = self.run_sh(self.installer)  # default: launcher + the bridge that is installed
        self.assertEqual(rc, 0, out); self.assertIn("SILENTGLASS 1.2.0 INSTALLED", out)
        self.assertEqual((self.app / "bin/BixbyPromotion.dll").read_bytes(), b"MZ new SilentGlass build")
        self.assertEqual(sorted(p.name for p in (self.app / "res/quiet-home").iterdir()), ["apps.json", "sym-new.svg"])
        self.assertEqual((self.q / "silentglass-ha.dll").read_bytes(), b"new bridge")
        self.assertIn("ExecStart=", (self.ud / "silentglass-ha.service").read_text())
        self.assertEqual(self.conf.read_text(), "host=broker\npassword=secret\n")  # settings never touched
        self.assertFalse((self.stage / "package.tar.gz").exists())
        name = re.search(r"undo: /bin/sh -s -- (silentglass-1\.2\.0-\d{8}T\d{6})", out).group(1)
        installed = self.now()
        # the same bundle again changes nothing and leaves no backup folder behind
        rc, out = self.run_sh(self.installer)
        self.assertEqual(rc, 0, out); self.assertIn("launcher: already 1.2.0", out); self.assertIn("bridge: already 1.2.0", out)
        self.assertIn("nothing to change", out); self.assertEqual(self.now(), installed)
        self.assertEqual(sorted(p.name for p in self.backups.iterdir()), [name])
        # undo puts back exactly what was there
        rc, out = self.run_sh(DEV / "undo.sh", name, stage=False)
        self.assertEqual(rc, 0, out); self.assertIn("SILENTGLASS INSTALL UNDONE", out)
        self.assertEqual(self.now(), self.before)
        self.assertEqual(self.conf.read_text(), "host=broker\npassword=secret\n")
        rc, out = self.run_sh(DEV / "undo.sh", name, stage=False)
        self.assertNotEqual(rc, 0); self.assertIn("already undone", out)

    def test_all_adds_the_bridge_and_undo_removes_it(self):
        shutil.rmtree(self.q); (self.ud / "silentglass-ha.service").unlink(); self.before = self.now()
        rc, out = self.run_sh(self.installer)  # default: no bridge installed -> launcher only
        self.assertEqual(rc, 0, out); self.assertFalse(self.q.exists())
        name1 = re.search(r"-- (silentglass-1\.2\.0-\d{8}T\d{6})", out).group(1)
        rc, out = self.run_sh(self.installer, "all")
        self.assertEqual(rc, 0, out); self.assertIn("launcher: already 1.2.0", out)
        self.assertEqual((self.q / "silentglass-ha.dll").read_bytes(), b"new bridge")
        self.assertTrue((self.ud / "default.target.wants/silentglass-ha.service").is_symlink())
        name2 = re.search(r"-- (silentglass-1\.2\.0-\d{8}T\d{6})", out).group(1)
        rc, out = self.run_sh(DEV / "undo.sh", name2, stage=False)
        self.assertEqual(rc, 0, out); self.assertIn("bridge: removed", out)
        self.assertFalse((self.q / "silentglass-ha.dll").exists()); self.assertFalse((self.ud / "silentglass-ha.service").exists())
        rc, out = self.run_sh(DEV / "undo.sh", name1, stage=False)
        self.assertEqual(rc, 0, out); self.assertEqual(self.now(), self.before)

    def test_refusals_leave_the_tv_untouched(self):
        # a changed file in the bundle
        work = self.tmp / "bad"; work.mkdir()
        subprocess.run(["tar", "-xzf", str(self.bundle), "-C", str(work)], check=True)
        (work / "bridge/silentglass-ha.dll").write_bytes(b"tampered")
        subprocess.run(["tar", "-czf", str(self.bundle), "-C", str(work), "."], check=True)
        rc, out = self.run_sh(self.installer, "all")
        self.assertNotEqual(rc, 0); self.assertIn("is not SilentGlass 1.2.0 - refusing (nothing changed)", out)
        self.assertEqual(self.now(), self.before); self.assertEqual(list(self.backups.iterdir()), [])
        # not a SilentGlass install (Samsung's own app): the first install is a separate, manual step
        (self.app / "bin/BixbyPromotion.dll").write_bytes(b"MZ Samsung original"); shutil.rmtree(self.app / "res/quiet-home")
        before = self.now()
        rc, out = self.run_sh(self.installer)
        self.assertNotEqual(rc, 0); self.assertIn("recipes/install-launcher.sh", out); self.assertEqual(self.now(), before)
        # undo refuses a folder that isn't one of install.sh's, and one whose launcher changed since
        rc, out = self.run_sh(DEV / "undo.sh", "../etc", stage=False)
        self.assertNotEqual(rc, 0); self.assertIn("refusing", out)

    def test_undo_refuses_when_the_launcher_changed_since(self):
        rc, out = self.run_sh(self.installer)
        name = re.search(r"-- (silentglass-1\.2\.0-\d{8}T\d{6})", out).group(1)
        (self.app / "bin/BixbyPromotion.dll").write_bytes(b"MZ another SilentGlass build")
        before = self.now()
        rc, out = self.run_sh(DEV / "undo.sh", name, stage=False)
        self.assertNotEqual(rc, 0); self.assertIn("the launcher changed since this install - refusing", out)
        self.assertEqual(self.now(), before)


class CommittedInstaller(unittest.TestCase):
    def test_lists_this_source_and_fits_the_runner(self):
        text = (DEV / "install.sh").read_text()
        self.assertLess(len(text.encode()), 64 * 1024)  # the TV runner takes at most 64 KiB on stdin
        self.assertIn(f"VER={bundle.version()}\n", text)
        listed = dict((l.split("  ", 1)[1], l.split("  ", 1)[0]) for l in re.search(r"<<'SUMS'\n(.*?)\nSUMS\n", text, re.S).group(1).splitlines())
        self.assertNotIn("quiet-home/apps.local.json", listed)
        self.assertFalse([k for k in listed if k.startswith(bundle.LOCAL)])  # never the owner's local assets
        self.assertEqual(listed["bridge/silentglass-ha.service"], sha(DEV / "ha-bridge/silentglass-ha.service"))
        self.assertEqual(listed["quiet-home/apps.json"], sha(ROOT / "launcher/assets/apps.json"))
        build = ROOT / ".local/build/ha-bridge/silentglass-ha.dll"  # reproducible: CI builds it before the tests
        if build.exists():
            self.assertEqual(listed["bridge/silentglass-ha.dll"], sha(build), "rebuild the bridge and run tools/bundle.py --public")
        dll = ROOT / ".local/package/BixbyPromotion.dll"  # the launcher needs the TV framework; not built in CI
        if dll.exists():
            self.assertEqual(listed["BixbyPromotion.dll"], sha(dll), "run tools/bundle.py --public after a launcher build")
        for s in ("install.sh", "undo.sh"):
            with (DEV / s).open() as f:
                self.assertEqual(subprocess.run(["sh", "-n"], stdin=f).returncode, 0)


if __name__ == "__main__":
    unittest.main()
