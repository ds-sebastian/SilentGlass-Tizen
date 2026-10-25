"""S90D port configuration checks, not a substitute for on-TV rendering tests."""
import csv
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "launcher/src/Home.cs"
SRC = ROOT / "launcher/src"
PARTS = ("Home.cs", "Home.Controls.cs", "Home.Inputs.cs", "Home.Info.cs", "Home.Idle.cs", "Home.State.cs")


def launcher():
    return "\n".join((SRC / p).read_text() for p in PARTS)


def body(source, signature):
    """Text of one C# member: from its signature to the closing brace at one-space indent."""
    start = source.index(signature)
    end = source.find("\n }\n", start)
    return source[start:end if end > 0 else None]
ASSETS = ROOT / "launcher/assets"
INVENTORY = ROOT / ".local/device-inventory/catalog-apps.csv"


class S90dPortTests(unittest.TestCase):
    def test_all_partials_are_compiled(self):
        proj = (SRC / "Home.csproj").read_text()
        for p in PARTS:
            self.assertIn(f'<Compile Include="{p}" />', proj)

    def test_primary_shield_uses_observed_source(self):
        src = launcher()
        self.assertIn('Hero("Watch","hdmi:15","SHIELD"', src)
        self.assertIn('lastSource=15', src)

    def test_secondary_keeps_the_tvs_stored_label(self):
        self.assertIn('Hero("Play","hdmi:13","Nintendo Switch"', launcher())

    def test_example_console_routes_are_removed(self):
        src = launcher()
        self.assertNotIn('"hdmi:14"', src)
        self.assertNotIn('"PlayStation 5"', src)

    def test_samsung_home_escape_and_quick_settings(self):
        src = launcher()
        self.assertIn('Id="com.samsung.tv.csfs",Asset="tile-samsung-home.svg"', src)
        self.assertIn('Control("More","page:2","more",4);', src)

    def test_home_is_shield_first_without_tv_apps(self):
        src = launcher()
        home = body(src, "void BuildHome()")
        # TV apps are backups: none of them is a target on Home, only the two inputs and controls
        self.assertNotIn('"app:', home)
        self.assertEqual(home.count("Hero("), 2)
        self.assertEqual(home.count("Control("), 5)
        self.assertIn('Control("Inputs","inputs","inputs",0);', home)
        self.assertIn('Control("Ambient","ambient","ambient",1);', home)
        # owner: the app chips inside the Watch card looked messy - the card is art only
        self.assertNotIn("HeroApps", src)
        self.assertIn("bool on=type==lastSource", src)  # "On now" follows the real input
        for noisy in ("Input name saved", "Direct input", "Everyday controls", "Back to input", "Most opened", "Select · ", "Let's watch"):
            self.assertNotIn(noisy, src)

    def test_catalog_only_has_installed_enabled_apps(self):
        rows = list(csv.DictReader(INVENTORY.read_text().splitlines()))
        enabled = {row["app_id"] for row in rows if row["app_disable"] == "false"}
        local = ASSETS / "apps.local.json"
        catalog = json.loads((local if local.exists() else ASSETS / "apps.json").read_text())
        ids = [app["Id"] for app in catalog]
        self.assertEqual(len(ids), len(set(ids)))
        added_after_inventory = {"rTn6ClEurC.Jellyfin"}  # owner-installed 2026-10-24, verified in TV registry
        self.assertTrue(set(ids) <= enabled | added_after_inventory, set(ids) - enabled)
        self.assertEqual(ids[:2], ["com.samsung.tv.cobalt-yt", "rTn6ClEurC.Jellyfin"])
        self.assertNotIn("HEPsqFNie0.tvplusstandalone", ids)
        # Uninstalled from this TV on 2026-10-24 at the owner's request.
        for removed in ("org.tizen.netflix-app", "MCmYXNxgcu.DisneyPlus", "3KA0pm7a7V.TubiTV"):
            self.assertNotIn(removed, ids)
        self.assertNotIn("org.tizen.netflix-app", launcher())
        for app in catalog:
            self.assertTrue((ASSETS / app["Asset"]).is_file())

    def test_new_artwork_is_local_valid_svg(self):
        # Fixed locally authored SVGs only; no uploads, remote XML, or DTDs are accepted.
        for name in ("feature-watch.svg", "feature-play.svg", "tile-samsung-home.svg", "free-background.svg"):
            tree = ET.parse(ASSETS / name)
            self.assertTrue(tree.getroot().tag.endswith("svg"))
            self.assertNotIn("https://", (ASSETS / name).read_text())
        watch = (ASSETS / "feature-watch.svg").read_text()
        self.assertNotIn("<text", watch)
        self.assertNotIn("<image", watch)  # drawn art, no embedded app logos

    def test_settings_tiles_have_valid_local_artwork(self):
        import re
        tiles = re.findall(r'SettingsTile\("([^"]+)","([^"]+)","[^"]*","([a-z]+)",(\d),(\d)\)', launcher())
        self.assertEqual(len(tiles), 12)
        self.assertEqual(sorted((int(c), int(r)) for *_, c, r in tiles), [(c, r) for c in range(4) for r in range(3)])
        for _, _, icon, _, _ in tiles:
            path = ASSETS / f"glyph-{icon}.svg"
            self.assertTrue(ET.parse(path).getroot().tag.endswith("svg"), path)
            self.assertNotIn("https://", path.read_text())

    def test_cycled_settings_are_refreshed_and_audio_format_is_not_a_tile(self):
        import re
        src = launcher()
        ids = re.search(r' SettingIds=\{([^}]*)\}', src).group(1)
        home_ids = re.search(r'HomeSettingIds=\{([^}]*)\}', src).group(1)
        for cycled in re.findall(r'"cycle:([a-z-]+)"', src):
            self.assertIn(f'"{cycled}"', ids)
        self.assertIn('"soundoutput"', ids)
        # Home reads only what its tiles show, and only ids the Controls page also knows
        self.assertEqual(set(re.findall(r'"([a-z-]+)"', home_ids)), {"sleeptimer", "soundoutput"})
        self.assertTrue(set(re.findall(r'"([a-z-]+)"', home_ids)) <= set(re.findall(r'"([a-z-]+)"', ids)))
        # Pass-Through feeds Dolby Atmos to the soundbar; it must not be one press away.
        self.assertNotIn("audio-format", src)

    def test_toolbar_area_does_not_collide_with_third_settings_row(self):
        src = launcher()
        self.assertIn("const int TB=9;", src)
        self.assertNotIn("area==3", src)
        self.assertNotIn("area=3", src)

    def test_every_drawn_icon_exists_and_local_catalog_has_no_placeholders(self):
        import json, re
        src = launcher()
        glyphs = re.findall(r'Control\("[^"]+","[^"]+","([a-z]+)",\d\)', src)
        self.assertEqual(len(glyphs), 5)
        for glyph in glyphs:
            ET.parse(ASSETS / f"glyph-{glyph}.svg")
        local = ASSETS / "apps.local.json"
        if not local.exists():
            self.skipTest("no device catalog")
        for app in json.loads(local.read_text()):
            if app.get("Utility"):
                # the utility plus is drawn in the (light) ink colour; its tile must be dark
                n = int(app["Background"].lstrip("#"), 16)
                self.assertLess(sum((n >> s) & 255 for s in (16, 8, 0)) / 3, 96, app["Name"])
                continue
            self.assertNotIn("placeholders/", app["Asset"], app["Name"])
            text = (ASSETS / app["Asset"]).read_text()
            self.assertNotIn("<text", text, app["Name"])
            self.assertNotRegex(text, r"href|https?://(?!www\.w3\.org)", app["Name"])
            # Repo-generated local assets; stdlib expat does not fetch external entities.
            ET.fromstring(text)

    def test_info_page_navigation_and_tab_bar(self):
        src = launcher()
        self.assertIn('string[] nav={"Home","Apps","Controls","Info"}', src)
        self.assertIn("int next=Math.Max(0,Math.Min(3,page+d));", src)
        self.assertNotIn("Toolbar(", src)
        build_info = src.split("void BuildInfo()")[1].split("void InfoTick()")[0]
        self.assertIn('"Thread border router"', build_info)
        self.assertNotIn("UsageSummary", build_info)
        self.assertNotIn("RecordLaunch", src)
        # the info timer must not run while another page or app is visible
        self.assertIn("infoTimer?.Stop();", body(src, "void BuildPage("))
        self.assertIn("if(page!=3)return false;", src)
        self.assertIn("clockTimer?.Stop();infoTimer?.Stop();", body(src, "protected override void OnPause()"))

    def test_info_page_lists_match_applied_privacy_state(self):
        info = (SRC / "InfoStats.cs").read_text()
        self.assertEqual(info.count('"com.samsung.tv.') + info.count('"org.tizen.') + info.count('"AQKO') + info.count('"HEPsq') + info.count('"pisa-service"') + info.count('"canalysis-daemon"') + info.count('"slive-provider-daemon"'), 34)
        self.assertNotIn("File.Delete", info.split("RecordLaunch")[0])
        # SMACK: marker files are unreadable to the app and other apps' /proc is hidden; never infer from those.
        self.assertNotIn("File.Exists(\"/opt/usr/share/systemd", info)
        self.assertNotIn("attr/current", info)
        self.assertIn("if (AppEnabled(selfId) != true) return -1;", info)
        src = launcher()
        self.assertIn('infoMarkers<0?"Unavailable"', src)
        self.assertIn('infoDisabled<0?"Unavailable"', src)

    def test_info_live_state_survives_card_select(self):
        build_info = launcher().split("void BuildInfo()")[1].split("void InfoTick()")[0]
        self.assertIn("bool fresh=Environment.TickCount64-lastNetAt>10000;", build_info)
        self.assertNotIn("lastNet=null;lastCpu=null;\n  InfoTick();", build_info)
        # every live row is populated at build time, never a placeholder until the next tick
        self.assertNotIn('"Since start",null)', build_info)
        self.assertIn('"Since start",TotalsText(InfoStats.NetDev()))', build_info)

    def test_oled_idle_dim(self):
        src = launcher()
        self.assertIn("const long IdleMs=5*60*1000;", src)
        # idle check only runs from the clock timer, which is stopped while Quiet Home is in the background
        self.assertIn("IdleTick();return true;", src)
        on_pause = body(src, "protected override void OnPause()")
        self.assertIn("CloseInputs(false);clockTimer?.Stop();", on_pause)
        # first key press only wakes, never activates a tile
        on_key = body(src, "void OnKey(")
        self.assertLess(on_key.index("if(dimmed){Wake(true);"), on_key.index("HandleKey(k)"))
        self.assertIn("Wake(false);", body(src, "protected override void OnResume()"))
        dim = src.split("void Dim()")[1].split("void MoveDimClock()")[0]
        self.assertIn("infoTimer?.Stop();", dim)
        self.assertIn("new Color(0,0,0,1)", dim)
        self.assertIn("overlay.Add(dimLayer)", dim)  # above the sheet and toast

    def test_inputs_sheet(self):
        src = launcher()
        # every key is consumed by the open sheet before page navigation; Back closes it
        handle = body(src, "void HandleKey(")
        self.assertTrue(handle.split("\n")[2].strip().startswith("if(inputsLayer!=null&&HandleInputsKey(k))return;"))
        sheet = body(src, "bool HandleInputsKey(")
        self.assertIn("SelectSource(s.Type)", sheet)
        self.assertIn("CloseInputs(true)", sheet)
        self.assertIn("CloseInputs(false);", body(src, "protected override void OnPause()"))
        # read-only source API only: names come from the TV, never renamed from here
        info = (SRC / "InfoStats.cs").read_text()
        self.assertIn("static extern string get_source_list();", info)  # marshaller frees the native buffer
        self.assertNotIn("set_edit_name", info + src)
        # powerState is unreliable on this TV (the on-screen SHIELD reports Off), so it is never shown
        self.assertNotIn(".Power==", src)
        # connection states are only claimed when the TV actually answered
        self.assertIn("src.Known&&!src.Connected", src)
        self.assertIn("s.Known&&!s.Connected", src)
        for g in ("glyph-inputs.svg", "glyph-ambient.svg", "glow-watch.svg", "glow-play.svg"):
            ET.parse(ASSETS / g)

    def test_inputs_are_event_driven_and_on_the_ui_thread(self):
        src = launcher()
        watch = body(src, "void WatchInputs()")
        self.assertIn("register_sourcelist_changed_callback(sourceListCallback", watch)
        self.assertIn('vconf_notify_key_changed("memory/system/source_type",sourceTypeCallback', watch)
        # native callbacks only hop onto the UI thread; delegates are fields so the GC keeps them
        self.assertEqual(watch.count("OnUi(()=>"), 2)
        self.assertIn("SourceListChangedCallback sourceListCallback;", src)
        self.assertIn("VconfChangedCallback sourceTypeCallback;", src)
        self.assertNotIn("new Tizen.NUI.Timer(", body(src, "void WatchInputs()"))  # no polling

    def test_key_path_has_no_io_or_ipc(self):
        src = launcher()
        for member in ("void UpdateFocus(", "void StyleSlot(", "void StyleTabs(", "void OnKey(", "void HandleKey("):
            text = body(src, member)
            for slow in ("WriteState(", "File.", "RefreshSettings(", "InfoStats.", "TileValue(", "Sources("):
                self.assertNotIn(slow, text, f"{slow} in {member}")
        # the state file is written by a coalescing timer, or immediately on pause only
        self.assertEqual(src.count("WriteState("), 3)  # definition, timer tick, OnPause - nowhere else
        self.assertIn("stateTimer=new Tizen.NUI.Timer(600)", src)

    def test_chrome_is_built_once(self):
        src = launcher()
        self.assertEqual(src.count('"free-background.svg"'), 1)
        self.assertIn('"free-background.svg"', body(src, "void BuildChrome()"))
        self.assertNotIn("BuildChrome()", body(src, "void BuildPage("))
        # page content is the only thing disposed on a page change
        self.assertIn("content.Remove(v);v.Dispose();", body(src, "void BuildPage("))

    def test_dark_theme_background(self):
        self.assertIn("Window.Default.BackgroundColor=new Color(0,0,0,1);", launcher())
        self.assertIn('fill="#000"', (ASSETS / "free-background.svg").read_text())


if __name__ == "__main__":
    unittest.main()
