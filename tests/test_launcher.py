"""SilentGlass launcher: the off-TV C# checks (tests/CatalogTests.cs) and the source-level rules that keep it fast and safe
on the TV. Not a substitute for on-TV tests; tests/test_s90d_screen.py simulates what is on screen."""
import csv
import json
import os
import re
import shutil
import subprocess
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "launcher/src"
ASSETS = ROOT / "launcher/assets"
BRIDGE = ROOT / "devices/s90d/device/ha-bridge/src"
INVENTORY = ROOT / ".local/device-inventory/catalog-apps.csv"
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet")
NUI_FREE = ("Shared.cs", "Logic.cs", "Qr.cs", "InfoStats.cs", "Screen.cs", "AppLibrary.cs")  # also built off the TV


def launcher():
    return "\n".join(p.read_text() for p in sorted(SRC.glob("*.cs")))


def body(source, signature):
    """Text of exactly one C# member: its signature through the brace that closes its body (braces in string/char
    literals and comments are skipped)."""
    start = source.index(signature)
    i, depth, quote = source.index("{", start), 0, None
    while i < len(source):
        ch = source[i]
        if quote:
            if ch == "\\":
                i += 1
            elif ch == quote:
                quote = None
        elif source.startswith("//", i):
            i = source.find("\n", i)
            if i < 0:
                break
        elif ch in "\"'":
            quote = ch
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
        i += 1
    raise AssertionError(f"unbalanced member: {signature}")


@unittest.skipUnless(DOTNET, "needs the .NET 6 SDK")
class OffTvChecks(unittest.TestCase):
    def test_catalog_logic_and_parsers(self):
        out = ROOT / ".local/catalog-tests"
        b = subprocess.run([DOTNET, "build", str(ROOT / "tests/CatalogTests.csproj"), "-c", "Release", "-o", str(out),
                            f"-p:BaseIntermediateOutputPath={ROOT / '.local/obj/CatalogTests'}/"], capture_output=True, text=True, timeout=600)
        self.assertEqual(b.returncode, 0, b.stdout[-2000:])
        r = subprocess.run([DOTNET, str(out / "CatalogTests.dll"), str(ASSETS / "apps.json")], capture_output=True, text=True, timeout=120)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.check_qr(out)

    def check_qr(self, out):
        # the setup sheet's QR code, module for module, against the qrcode library with the same version, level M,
        # byte mode and mask (skipped without it: uv run --with qrcode ...)
        try:
            import qrcode
        except ImportError:
            return
        texts = ["http://192.0.2.10:7680/?s=a1b2c3", "http://192.168.100.200:7680/?s=ffffff", "x" * 120, "\u00e9" * 60, "HELLO"]
        r = subprocess.run([DOTNET, str(out / "CatalogTests.dll"), "--qr", *texts], capture_output=True, text=True, timeout=60, check=True)
        for text, ours in zip(texts, r.stdout.strip().split("\n\n")):
            version = (len(ours.split("\n")) - 17) // 4
            refs = []
            for mask in range(8):
                q = qrcode.QRCode(version=version, error_correction=qrcode.constants.ERROR_CORRECT_M, border=0, mask_pattern=mask)
                q.add_data(qrcode.util.QRData(text.encode(), mode=qrcode.util.MODE_8BIT_BYTE)); q.make(fit=False)
                refs.append("\n".join("".join("1" if b else "0" for b in row) for row in q.get_matrix()))
            self.assertIn(ours, refs, text)


class LauncherSource(unittest.TestCase):
    def test_every_source_is_compiled_and_the_logic_builds_off_the_tv(self):
        proj = (SRC / "SilentGlass.csproj").read_text()
        for p in SRC.glob("*.cs"):
            self.assertIn(f'<Compile Include="{p.name}" />', proj)
        for name in NUI_FREE:
            self.assertNotRegex((SRC / name).read_text(), r"\bTizen\.|using Tizen", name)
        tests = (ROOT / "tests/CatalogTests.csproj").read_text() + (ROOT / "tests/screen-sim/ScreenSim.csproj").read_text()
        for name in NUI_FREE:
            self.assertIn(f"launcher/src/{name}", tests)

    def test_shared_contract_is_the_only_place_for_ids_and_file_names(self):
        # SilentGlass and the bridge exchange files and launch extras; both take the names from Shared.cs
        shared = (SRC / "Shared.cs").read_text()
        names = re.findall(r'(?:Self|Viewer|SamsungHome|Art|Extra\w+|\w+File|SetupRequest|SetupClose|BridgeStatus|NotifyOff|ThreadOff|ThreadSwitch|ThreadUnit) = "([^"]+)"', shared)
        self.assertGreaterEqual(len(names), 18)
        self.assertIn(f'<Compile Include="../../../../../launcher/src/Shared.cs" />', (BRIDGE / "HaBridge.csproj").read_text())
        for path in [*SRC.glob("*.cs"), *BRIDGE.glob("*.cs")]:
            if path.name == "Shared.cs":
                continue
            text = path.read_text()
            for name in names:
                self.assertFalse(f'"{name}"' in text, f"{path.name} spells out {name}: use Shared")
        # identifiers existing installs depend on
        for keep in ('Self = "com.samsung.tv.bixby-promotion"', 'StateFile = "/tmp/silentglass-state.txt"', 'PrefsFile = "silentglass-prefs.txt"',
                     'ThreadUnit = "/opt/usr/home/owner/.config/systemd/user/quiet-otbr-switch.path"'):
            self.assertIn(keep, shared)

    def test_windows_never_cover_the_picture_or_take_the_remote_unasked(self):
        ov, notice = (SRC / "Overlay.cs").read_text(), (SRC / "Notice.cs").read_text()
        ensure = body(ov, "void EnsureOverlay()")
        for line in ("ow.Type = WindowType.Notification;", "ow.SetNotificationLevel(NotificationLevel.Top);",
                     'ow.AddAuxiliaryHint("wm.policy.win.user.geometry", "1");', "ow.BackgroundColor = Clear;"):
            self.assertIn(line, ensure)
        self.assertIn("new Rectangle(0, BandTop, 1920, 1080 - BandTop)", ensure)  # never full screen over the picture
        # the banner never takes focus, never launches anything, never shows the backdrop
        self.assertIn("nw.SetAcceptFocus(false);", notice)
        self.assertNotIn("nw.SetAcceptFocus(true)", notice)
        for banned in ("Launch(", "ShowBackdrop", "connect_source", "Window.Default"):
            self.assertNotIn(banned, notice)
        # a notice image is a bare file name in our own data directory
        self.assertRegex(body(notice, "void TakeNotice("), r"image\.Contains\('/'\) \|\| image\.Contains\(\"\.\.\"\)")
        # the same title while fully shown refreshes in place (no second fade-in); anything else animates in
        show = body(notice, "void ShowNotice(")
        self.assertIn("titleText.Length > 0 && titleText == nTitle", show)
        self.assertIn("if (refresh) { nCard.Opacity = 1;", show)
        # the TV pauses us whenever only the overlay is up: a pause must not close it
        pause = body(launcher(), "protected override void OnPause()")
        self.assertNotIn("CloseOverlay", pause)

    def test_screen_decisions_live_in_screen_cs(self):
        # nothing else shows the wallpaper or reads the signal flag (tests/screen-sim runs Screen.cs against a simulated TV)
        for p in SRC.glob("*.cs"):
            if p.name in ("Screen.cs", "Tv.cs"):
                continue
            text = p.read_text()
            self.assertNotRegex(text, r"(?<!void )ShowBackdrop\(\)", p.name)
            self.assertNotIn("SignalKey", text, p.name)

    def test_key_path_has_no_io_or_ipc(self):
        src = launcher()
        for member in ("void OnKey(", "void HandleKey(", "void OverlayKey(", "void FocusBar(", "void FocusMenu()", "void FocusApps(", "void InputsKey(", "void PanelKey("):
            text = body(src, member)
            for slow in ("WriteState(", "File.", "RefreshSettings(", "InfoStats.", "Options(", "Cec."):
                self.assertNotIn(slow, text, f"{slow} in {member}")
        self.assertNotIn("UpdatePanel(true)", body(src, "void PanelKey("))  # a focus move re-reads nothing from the TV
        # the state file is written by a coalescing timer, or at once on pause
        self.assertEqual(len(re.findall(r"\bWriteState\(", src)), 3)  # definition, the timer, OnPause

    def test_native_callbacks_are_kept_alive_and_posted_to_the_ui_thread(self):
        tv = (SRC / "Tv.cs").read_text()
        callbacks = re.findall(r"(\w+Callback) = \([^)]*\) => (\w+)\(", tv)
        self.assertEqual(len(callbacks), 5)
        fields = {n.strip() for decl in re.findall(r"(?:SourceListChangedCallback|VconfChangedCallback) ([\w, ]+);", tv) for n in decl.split(",")}
        for field, call in callbacks:
            self.assertEqual(call, "OnUi", field)
            self.assertIn(field, fields)  # a field, not a local: native code keeps calling it
        self.assertIn("a.EndAction = Animation.EndActions.StopFinal;", launcher())  # an interrupted animation lands on its end

    def test_words_the_owner_asked_for(self):
        ui = "\n".join(p.read_text() for p in SRC.glob("*.cs") if p.name not in ("Shared.cs", "Logic.cs"))
        # no On/Off words; status is a dot or a switch; Game Mode is Automatic / Always / Never
        for word in ('"On"', '"Off"', '" On"', '"Not connected"', '"On now"'):
            self.assertNotIn(word, ui)
        self.assertNotIn("audio-format", ui)  # Pass-Through feeds Dolby Atmos to the soundbar: not one press away
        for name in ("Nintendo Switch", '"SHIELD"', "PlayStation"):  # the TV's own input names are used as they are
            self.assertNotIn(name, ui)

    def test_every_symbol_and_backdrop_asset_is_local_svg(self):
        src = launcher()
        names = set(re.findall(r'Sym\("([a-z]+)"', src)) | set(re.findall(r'(?<![\w.])(?:Level|Choice|Toggle|Open)\("([a-z]+)", "', src))
        names |= set(re.findall(r'\("([a-z]+)", "[A-Z]\w+", ', body(src, "void OpenBar(")))  # the bar's glyphs
        names |= {"tv", "gamecontroller", "antenna", "hdmi"}  # PortGlyph
        self.assertGreater(len(names), 20)
        files = [ASSETS / f"sym-{n}.svg" for n in names] + [ASSETS / "tile-samsung-home.svg", ASSETS / "free-background.svg"]
        for path in files:
            self.assertTrue(ET.parse(path).getroot().tag.endswith("svg"), path)
            self.assertNotIn("https://", path.read_text())

    def test_setup_sheet_shows_the_address_only(self):
        s = (SRC / "Setup.cs").read_text()
        self.assertNotIn("?code=", s)  # the code is only on the TV's screen: the phone must type it
        self.assertIn('url + "?s=" + session', s)  # a fresh address per sheet: phones reuse a tab with the same one
        self.assertIn("CloseSetupServer();", body(launcher(), "void CloseOverlay("))  # closing the overlay closes the page

    def test_deploy_pair_matches_the_version(self):
        version = re.search(r'ModVersion = "(v\d+\w)"', (SRC / "SilentGlass.cs").read_text()).group(1)
        deploy = ROOT / "devices/s90d/device/launcher-deploy"
        update = deploy / f"update-{version}.sh"
        self.assertTrue(update.exists(), update)
        prev = re.search(r"Inverse: restore-(v\d+\w)\.sh", update.read_text()).group(1)
        self.assertTrue((deploy / f"restore-{prev}.sh").exists())
        # only the current pair, plus the way back to Samsung Home
        self.assertEqual(sorted(p.name for p in deploy.glob("*.sh")), sorted([update.name, f"restore-{prev}.sh", "restore-stock-home.sh"]))
        for p in deploy.glob("*.sh"):
            r = subprocess.run(["sh", "-n"], stdin=p.open(), capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, f"{p.name}: {r.stderr}")


class DeviceRecord(unittest.TestCase):
    def test_catalog_only_has_installed_enabled_apps(self):
        if not INVENTORY.is_file():
            self.skipTest("private device inventory is not included in a clean checkout")
        rows = list(csv.DictReader(INVENTORY.read_text().splitlines()))
        enabled = {row["app_id"] for row in rows if row["app_disable"] == "false"}
        local = ASSETS / "apps.local.json"
        catalog = json.loads((local if local.exists() else ASSETS / "apps.json").read_text())
        ids = [app["Id"] for app in catalog]
        self.assertEqual(len(ids), len(set(ids)))
        added_after_inventory = {"rTn6ClEurC.Jellyfin"}  # owner-installed 2026-10-24, verified in TV registry
        self.assertTrue(set(ids) <= enabled | added_after_inventory, set(ids) - enabled)
        # uninstalled or disabled at the owner's request (2026-10-24/25)
        for removed in ("org.tizen.netflix-app", "MCmYXNxgcu.DisneyPlus", "3KA0pm7a7V.TubiTV", "HEPsqFNie0.tvplusstandalone",
                        "org.tizen.primevideo", "com.samsung.tv.aria-video", "com.samsung.tv.cobalt-yt", "org.tizen.browser"):
            self.assertNotIn(removed, ids)
        for app in catalog:
            self.assertTrue((ASSETS / app["Asset"]).is_file())

    def test_info_privacy_is_read_only_and_covers_every_registry_batch(self):
        info = (SRC / "InfoStats.cs").read_text()
        for write in ("File.Delete", "File.Write", "attr/current"):
            self.assertNotIn(write, info)
        # could not check (the self-check fails, a lookup errors): "Unknown", never a false "Check"
        self.assertIn("if (AppEnabled(selfId) != true) return null;", info)
        listed = set(re.findall(r'"([A-Za-z0-9._-]+)"', info.split("DisabledApps = {")[1].split("};")[0]))
        for batch in ("batchc-apply.sh", "batche-apply.sh", "batchf-apply.sh", "batchf2-apply.sh"):
            text = (ROOT / "devices/s90d/device/cleanup" / batch).read_text()
            ids = re.search(r'^IDS="([^"]+)"', text, re.M)  # batch F names its targets once
            scope = ids.group(1) if ids else text.split("app_id IN (")[1].split(")")[0]
            apps = set(re.findall(r"'([A-Za-z0-9._-]+)'", scope))
            self.assertTrue(apps and apps <= listed, (batch, apps - listed))
        # never anything the Thread border router or Samsung Home depends on
        for keep in ("smartthings", "iot-hub", "matter", "coba.adagent", "csfs"):
            self.assertNotIn(keep, (ROOT / "devices/s90d/device/cleanup/batche-apply.sh").read_text().split("set -eu")[1])

    def test_csfs_watcher_only_closes_unused_samsung_home(self):
        unit = (ROOT / "devices/s90d/device/boot-units/quiet-csfs-off.service").read_text()
        self.assertIn('[ "$$home" = com.samsung.tv.bixby-promotion ]', unit)
        self.assertIn('[ "$$fg" != com.samsung.tv.csfs ]', unit)
        self.assertIn("seen -ge 2", unit)


if __name__ == "__main__":
    unittest.main()
