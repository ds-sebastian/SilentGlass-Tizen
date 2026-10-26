"""SilentGlass launcher checks (source-level invariants; not a substitute for on-TV tests or the Pi mockups)."""
import csv
import json
import re
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "launcher/src"
BRIDGE = ROOT / "devices/s90d/device/ha-bridge/src"
PARTS = ("SilentGlass.cs", "Ui.cs", "Backdrop.cs", "Overlay.cs", "Menus.cs", "Panels.cs", "Setup.cs", "Notice.cs", "Tv.cs",
         "Logic.cs", "Qr.cs", "AppLibrary.cs", "InfoStats.cs")


def launcher():
    return "\n".join((SRC / p).read_text() for p in PARTS)


def bridge():
    return "\n".join(p.read_text() for p in sorted(BRIDGE.glob("*.cs")))


def body(source, signature):
    """Text of exactly one C# member: its signature through the brace that closes its body.
    Braces inside string/char literals and comments are skipped, so one-line members do not run into the next."""
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
            i = source.find("\n", i)  # comments (TV's, don't, ...) are not literals
            if i < 0:
                break
        elif source.startswith("/*", i):
            i = source.index("*/", i) + 1
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
ASSETS = ROOT / "launcher/assets"
INVENTORY = ROOT / ".local/device-inventory/catalog-apps.csv"


class LauncherTests(unittest.TestCase):
    def test_every_source_is_compiled_and_the_logic_is_testable_off_the_tv(self):
        proj = (SRC / "SilentGlass.csproj").read_text()
        self.assertEqual(sorted(p.name for p in SRC.glob("*.cs") if p.name != "QuickLaunch.cs"), sorted(PARTS))
        for p in PARTS:
            self.assertIn(f'<Compile Include="{p}" />', proj)
        tests = (ROOT / "tests/CatalogTests.csproj").read_text()
        for p in ("Logic.cs", "Qr.cs"):
            self.assertIn(f'<Compile Include="../launcher/src/{p}" />', tests)
            self.assertNotIn("Tizen.", (SRC / p).read_text())

    def test_body_helper_stops_at_the_member(self):
        src = "class C\n{\n void A(){x(\"}\"); // the TV's }\n }\n void B(){y();}\n}\n"
        self.assertEqual(body(src, "void A("), 'void A(){x("}"); // the TV\'s }\n }')

    def test_no_full_screen_pages(self):
        src = launcher()
        # owner: "get rid of our full screen app pages ... just do layovers and popups"
        for gone in ("BuildPage", "VideoCanvas", "TVWindowControl", "Shader", "Home.Live", "HomeLogic", "Hero(", "editing"):
            self.assertNotIn(gone, src)
        # the app's own window only ever holds the backdrop (wallpaper, clock, idle dim)
        backdrop = (SRC / "Backdrop.cs").read_text()
        self.assertEqual(src.count("Window.Default.Add("), 1)  # the dim layer; BuildBackdrop adds via `w`
        self.assertEqual(body(backdrop, "void BuildBackdrop()").count("w.Add("), 1)  # one root: wallpaper + clock
        build = body(backdrop, "void BuildBackdrop()")
        # not hidden at start: the TV's launch placeholder (holding the keys) only goes once this window was shown;
        # Home hides it when the overlay opens (as v31 did)
        self.assertNotIn("Hide()", build.replace("backdrop.Hide()", ""))
        self.assertIn("if (!backdropShown && !mainUp) return;", body(backdrop, "void HideBackdrop()"))
        # shown only when there is nothing to draw over
        home = body(src, "void Home(")
        self.assertIn("bool picture = HasPicture && (lastOther == Viewer || lastOther == \"\");", home)
        self.assertIn("if (picture && !backdropShown) HideBackdrop(); else ShowBackdrop();", home)
        # Home over the picture toggles the bar
        self.assertIn('if (open == "" && overlayShown && !backdropShown) { CloseOverlay(why + ":close"); return; }', home)

    def test_overlay_is_a_partial_notification_window_above_the_viewer(self):
        ov = (SRC / "Overlay.cs").read_text()
        ensure = body(ov, "void EnsureOverlay()")
        for line in ("ow.Type = WindowType.Notification;", "ow.SetNotificationLevel(NotificationLevel.Top);",
                     'ow.AddAuxiliaryHint("wm.policy.win.user.geometry", "1");', "ow.BackgroundColor = Clear;", "ow.SetAcceptFocus(true);"):
            self.assertIn(line, ensure)
        self.assertIn("new Rectangle(0, BandTop, 1920, 1080 - BandTop)", ensure)  # never full screen over the picture
        self.assertIn("oRoot.Position2D = new Position2D(-x, -y);", body(ov, "void Place("))
        # the package may raise notification windows
        manifest = (ROOT / "launcher/package/tizen-manifest.xml")
        if manifest.exists():
            self.assertIn("http://tizen.org/privilege/window.priority.set", manifest.read_text())
        # created at start, while the main window is still up, so it can take focus later (v40a's bar got no keys)
        self.assertIn("EnsureOverlay();  // created while the app's main window is still up", (SRC / "SilentGlass.cs").read_text())
        # the TV pauses us whenever only the overlay is up: a pause must not close it; another app in front does
        pause = body(launcher(), "protected override void OnPause()")
        self.assertIn("if (!overlayShown) HideBackdrop();", pause)
        self.assertNotIn("CloseOverlay", pause)
        self.assertIn('if (now != SelfId && now != Viewer && (overlayShown || backdropShown)) { CloseOverlay("foreground:" + now); HideBackdrop(); }', (SRC / "Tv.cs").read_text())

    def test_panels_replace_the_bar_and_menus_sit_above_it(self):
        src = launcher()
        build = body(src, "void BuildPanel(")
        self.assertIn("ClearOverlay();", build)  # the bar goes; the picture stays visible on the left
        self.assertIn("Place(wx, 0, 1920 - wx, 1080);", build)
        self.assertIn("int h = 24 + inputs.Count * MenuRowH, x = Math.Clamp(BarItemCentre(\"inputs\") - PopW / 2, 40, 1880 - PopW), y = BarTop - PopGap - h;", src)
        # Back from a menu or panel lands on the bar item that opened it
        self.assertIn('BackToBar(mode == Mode.Controls ? "controls" : "info")', src)
        self.assertIn('BackToBar("inputs")', src)
        self.assertIn('BackToBar("apps")', src)
        # Info is near-opaque for reading; Controls is translucent so the picture shows the change
        self.assertIn('BuildPanel(Mode.Info, "SilentGlass " + ModVersion, true, list);', src)
        self.assertRegex(body(src, "void OpenControls()"), r"BuildPanel\(Mode\.Controls, null, false,")

    def test_watch_and_play_with_the_device_underneath(self):
        src = launcher()
        self.assertIn('("tv", "Watch", InputName(watchPort), "hdmi:" + watchPort), ("gamecontroller", "Play", InputName(playPort), "hdmi:" + playPort)', src)
        self.assertIn('watchPort = prefs.Watch != 0 ? prefs.Watch : PortNamed("shield", 15);', src)
        self.assertIn('playPort = prefs.Play != 0 ? prefs.Play : PortNamed("switch", 13);', src)
        self.assertIn("prefs.Watch = Logic.NextPort(prefs.Watch, playPort, dir)", src)
        # no device names baked into the UI: the TV's input names are used as they are
        for name in ("Nintendo Switch", '"SHIELD"', "PlayStation"):
            self.assertNotIn(name, src)
        # apps can be kept out of the bar
        self.assertIn("if (!prefs.HideApps && library.Count > 0) items.Add((\"apps\", \"Apps\", null, \"apps\"));", src)

    def test_words_the_owner_asked_for(self):
        src = launcher()
        ui = src.replace((SRC / "Logic.cs").read_text(), "")  # Logic maps the TV's own words
        # no On/Off words; status is a dot or a switch; Game Mode is Automatic / Always / Never
        for word in ('"On"', '"Off"', '" On"', '"Not connected"', '"On now"'):
            self.assertNotIn(word, ui)
        self.assertIn('"Game Mode", () => Logic.GameName(SettingName("game-mode-auto"))', src)
        self.assertIn('CycleSetting("game-mode-auto", d)', src)
        # Pass-Through feeds Dolby Atmos to the soundbar; it must not be one press away
        self.assertNotIn("audio-format", src)
        # every drawn symbol exists, local SVG only
        for name in set(re.findall(r'Sym\("([a-z]+)"', src)) | set(re.findall(r'(?<![\w.])(?:Level|Choice|Toggle|Open)\("([a-z]+)", "', src)) | {"tv", "gamecontroller", "antenna", "hdmi", "inputs", "controls", "apps", "info"}:
            path = ASSETS / f"sym-{name}.svg"
            self.assertTrue(ET.parse(path).getroot().tag.endswith("svg"), path)
            self.assertNotIn("https://", path.read_text())

    def test_key_path_has_no_io_or_ipc(self):
        src = launcher()
        for member in ("void OnKey(", "void HandleKey(", "void OverlayKey(", "void FocusBar(", "void FocusMenu()", "void FocusApps(", "void InputsKey("):
            text = body(src, member)
            for slow in ("WriteState(", "File.", "RefreshSettings(", "InfoStats.", "Options("):
                self.assertNotIn(slow, text, f"{slow} in {member}")
        # the state file is written by a coalescing timer, or at once on pause only
        self.assertEqual(src.count("WriteState("), 3)  # definition, timer tick, OnPause
        self.assertIn("stateTimer = new Tizen.NUI.Timer(600)", src)

    def test_interrupted_animation_lands_on_its_end(self):
        self.assertIn("a.EndAction = Animation.EndActions.StopFinal;", launcher())

    def test_native_callbacks_are_kept_alive_and_posted_to_the_ui_thread(self):
        tv = (SRC / "Tv.cs").read_text()
        self.assertIn("SourceListChangedCallback sourceListCallback; VconfChangedCallback sourceTypeCallback, signalCallback, foregroundCallback;", tv)
        for cb in ("sourceListCallback = (t, d) => OnUi(", "sourceTypeCallback = (n, d) => OnUi(", "signalCallback = (n, d) => OnUi(", "foregroundCallback = (n, d) => OnUi("):
            self.assertIn(cb, tv)
        # the signal flag flaps while an input syncs
        self.assertIn("signalSettle = new Tizen.NUI.Timer(800)", tv)

    def test_ambient_home_returns_to_silentglass(self):
        self.assertIn('if (now == "com.samsung.tv.csfs" && IsAmbient(before)) { Launch(SelfId);', (SRC / "Tv.cs").read_text())

    def test_oled_idle_dim_only_on_the_backdrop(self):
        b = (SRC / "Backdrop.cs").read_text()
        self.assertIn("const long IdleMs = 5 * 60 * 1000;", b)
        self.assertIn("if (prefs.Dim && backdropShown && Environment.TickCount64 - lastActivity >= IdleMs) Dim();", b)
        self.assertIn("if (dimmed) { Wake(); return; }  // the first press only wakes", launcher())

    def test_notifications_never_take_focus_or_wake(self):
        n = (SRC / "Notice.cs").read_text()
        self.assertIn("nw.SetAcceptFocus(false);", n)
        self.assertIn("if (image.Contains('/') || image.Contains(\"..\")", n)  # bare file names in our data dir only
        self.assertIn("NoticeMaxAgeMs", n)
        self.assertNotIn("Launch(", n)

    def test_launcher_and_bridge_agree(self):
        src, br = launcher(), bridge()
        # launch extras
        for key in ("sg_notice", "sg_source"):
            self.assertIn(f'"{key}"', src); self.assertIn(f'"{key}"', br)
        self.assertIn('"sg_open", "controls"'.replace(", ", ","), (SRC / "QuickLaunch.cs").read_text().replace(", ", ","))
        # files in the launcher's data directory
        for f in ("notice.json", "setup.json", "setup-request", "setup-close", "bridge-status.txt", "location.txt", "notify-off", "thread-off"):
            self.assertIn(f, src, f); self.assertIn(f, br, f)
        # the state file the bridge reads for the app on screen
        self.assertIn('"/tmp/silentglass-state.txt"', src); self.assertIn('"/tmp/silentglass-state.txt"', br)
        for key in ('Put("ui"', 'Put("backdrop"', 'Put("fg"'):
            self.assertIn(key, src)
        self.assertIn('V("ui") == "none" && V("backdrop") == "hidden"', br)

    def test_setup_sheet(self):
        s = (SRC / "Setup.cs").read_text()
        self.assertIn('File.WriteAllText(file, Qr.Svg(Qr.Encode(url + "?code=" + code)));', s)
        self.assertIn('string file = DataFile("setup-qr-" + (++qrN) + ".svg");', s)  # DALi caches by URL
        self.assertIn("if (back) CloseSetup();", (SRC / "Overlay.cs").read_text())
        # closing the overlay any way also closes the phone page
        self.assertIn("CloseSetupServer();", body(launcher(), "void CloseOverlay("))

    def test_deploy_pair_matches_the_version(self):
        version = re.search(r'ModVersion = "(v\d+\w)"', (SRC / "SilentGlass.cs").read_text()).group(1)
        deploy = ROOT / "devices/s90d/device/launcher-deploy"
        update = deploy / f"update-{version}.sh"
        self.assertTrue(update.exists(), update)
        prev = re.search(r"Inverse: restore-(v\d+\w)\.sh", update.read_text()).group(1)
        self.assertTrue((deploy / f"restore-{prev}.sh").exists())
        self.assertEqual(len(list(deploy.glob("*.sh"))), 2)  # only the current pair

    def test_backdrop_art_is_local_svg(self):
        for name in ("tile-samsung-home.svg", "free-background.svg"):
            tree = ET.parse(ASSETS / name)
            self.assertTrue(tree.getroot().tag.endswith("svg"))
            self.assertNotIn("https://", (ASSETS / name).read_text())
        self.assertIn('fill="#000"', (ASSETS / "free-background.svg").read_text())
        self.assertIn('Id = "com.samsung.tv.csfs", Asset = "tile-samsung-home.svg"', launcher())

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
        self.assertEqual(ids[0], "rTn6ClEurC.Jellyfin")
        self.assertNotIn("HEPsqFNie0.tvplusstandalone", ids)
        # Uninstalled from this TV on 2026-10-24 at the owner's request.
        # 2026-10-25: Prime Video and Apple TV uninstalled, TV YouTube disabled (cast to SHIELD instead)
        for removed in ("org.tizen.netflix-app", "MCmYXNxgcu.DisneyPlus", "3KA0pm7a7V.TubiTV",
                        "org.tizen.primevideo", "com.samsung.tv.aria-video", "com.samsung.tv.cobalt-yt", "org.tizen.browser"):
            self.assertNotIn(removed, ids)
        self.assertNotIn("org.tizen.netflix-app", launcher())
        for app in catalog:
            self.assertTrue((ASSETS / app["Asset"]).is_file())

    def test_info_privacy_is_read_only_and_unknown_is_not_a_failure(self):
        info = (SRC / "InfoStats.cs").read_text()
        self.assertNotIn("File.Delete", info)
        self.assertNotIn("File.Write", info)
        self.assertNotIn("attr/current", info)
        self.assertIn("if (AppEnabled(selfId) != true) return -1;", info)
        self.assertIn('if (marks < 0 || apps < 0) return ("Unknown", Grey);', launcher())

    def test_info_privacy_list_covers_every_registry_batch(self):
        info = (SRC / "InfoStats.cs").read_text()
        listed = set(re.findall(r'"([A-Za-z0-9._-]+)"', info.split("DisabledApps = {")[1].split("};")[0]))
        for batch in ("batchc-apply.sh", "batche-apply.sh", "batchf-apply.sh"):
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
