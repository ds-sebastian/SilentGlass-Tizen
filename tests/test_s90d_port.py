"""S90D port configuration checks, not a substitute for on-TV rendering tests."""
import csv
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "launcher/src/Home.cs"
ASSETS = ROOT / "launcher/assets"
INVENTORY = ROOT / ".local/device-inventory/catalog-apps.csv"


class S90dPortTests(unittest.TestCase):
    def test_primary_shield_uses_observed_source(self):
        self.assertIn('Hero("Watch","hdmi:15","SHIELD"', SOURCE.read_text())
        self.assertIn('lastSource=15;', SOURCE.read_text())

    def test_secondary_keeps_the_tvs_stored_label(self):
        self.assertIn('Hero("Play","hdmi:13","Nintendo Switch"', SOURCE.read_text())

    def test_example_console_routes_are_removed(self):
        source = SOURCE.read_text()
        self.assertNotIn('"hdmi:14"', source)
        self.assertNotIn('"PlayStation 5"', source)

    def test_samsung_home_escape_and_quick_settings(self):
        source = SOURCE.read_text()
        # Samsung Home moved to the Apps page; Home keeps a one-press way to all controls
        self.assertIn('Id="com.samsung.tv.csfs",Asset="tile-samsung-home.svg"', source)
        self.assertIn('Control("More","page:2","more",4);', source)

    def test_home_is_shield_first_without_tv_apps(self):
        source = SOURCE.read_text()
        home = source.split("if(page==0){\n   RefreshSettings();")[1].split("}else if(page==1){")[0]
        # TV apps are backups: none of them is a target on Home, only the two inputs and controls
        self.assertNotIn('"app:', home)
        self.assertEqual(home.count("Hero("), 2)
        self.assertEqual(home.count("Control("), 5)
        self.assertIn('Control("Inputs","inputs","inputs",0);', home)
        self.assertIn('Control("Ambient","ambient","ambient",1);', home)
        # the apps that live on SHIELD are shown inside the Watch card
        self.assertIn('HeroApps(watch,"rTn6ClEurC.Jellyfin","com.samsung.tv.cobalt-yt");', home)
        self.assertIn('if(action=="hdmi:"+lastSource)', source)  # "On now" badge follows the real input
        for noisy in ("Input name saved", "Direct input", "Everyday controls", "Back to input", "Most opened", "Select · ", "Let's watch"):
            self.assertNotIn(noisy, source)

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
        self.assertIn('HeroApps(watch,"rTn6ClEurC.Jellyfin"', SOURCE.read_text())
        self.assertNotIn("HEPsqFNie0.tvplusstandalone", ids)
        # Uninstalled from this TV on 2026-10-24 at the owner's request.
        for removed in ("org.tizen.netflix-app", "MCmYXNxgcu.DisneyPlus", "3KA0pm7a7V.TubiTV"):
            self.assertNotIn(removed, ids)
        self.assertIn("com.samsung.tv.cobalt-yt", ids)
        self.assertNotIn("org.tizen.netflix-app", SOURCE.read_text())
        for app in catalog:
            self.assertTrue((ASSETS / app["Asset"]).is_file())

    def test_new_artwork_is_local_valid_svg(self):
        # Fixed locally authored SVGs only; no uploads, remote XML, or DTDs are accepted.
        for name in ("feature-watch.svg", "feature-play.svg", "tile-samsung-home.svg", "free-background.svg"):
            tree = ET.parse(ASSETS / name)
            self.assertTrue(tree.getroot().tag.endswith("svg"))
            self.assertNotIn("https://", (ASSETS / name).read_text())


    def test_settings_tiles_have_valid_local_artwork(self):
        import re
        source = SOURCE.read_text()
        tiles = re.findall(r'SettingsTile\("([^"]+)","([^"]+)","[^"]*","([a-z]+)",(\d),(\d)\)', source)
        self.assertEqual(len(tiles), 12)
        self.assertEqual(sorted((int(c), int(r)) for *_, c, r in tiles), [(c, r) for c in range(4) for r in range(3)])
        for _, _, icon, _, _ in tiles:
            path = ASSETS / f"glyph-{icon}.svg"
            self.assertTrue(ET.parse(path).getroot().tag.endswith("svg"), path)
            self.assertNotIn("https://", path.read_text())

    def test_cycled_settings_are_refreshed_and_audio_format_is_not_a_tile(self):
        import re
        source = SOURCE.read_text()
        ids = re.search(r'SettingIds=\{([^}]*)\}', source).group(1)
        for cycled in re.findall(r'"cycle:([a-z-]+)"', source):
            self.assertIn(f'"{cycled}"', ids)
        self.assertIn('"soundoutput"', ids)
        # Pass-Through feeds Dolby Atmos to the soundbar; it must not be one press away.
        self.assertNotIn("audio-format", source)

    def test_toolbar_area_does_not_collide_with_third_settings_row(self):
        source = SOURCE.read_text()
        self.assertIn("const int TB=9;", source)
        self.assertNotIn("area==3", source)
        self.assertNotIn("area=3", source)

    def test_every_drawn_icon_exists_and_local_catalog_has_no_placeholders(self):
        import json, re
        source = SOURCE.read_text()
        for icon in re.findall(r'Toolbar\("[^"]+","[^"]+","([a-z_]+)"', source):
            self.assertGreater((ASSETS / f"symbol-{icon}.png").stat().st_size, 200)
        glyphs = re.findall(r'Control\("[^"]+","[^"]+","([a-z]+)",\d\)', source)
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
        source = SOURCE.read_text()
        self.assertIn('string[] nav={"Home","Apps","Controls","Info"}', source)
        self.assertIn("int next=Math.Max(0,Math.Min(3,page+d));", source)
        self.assertNotIn("Toolbar(", source)
        # Thread border router status replaces the usage panel
        build_info = source.split("void BuildInfo()")[1].split("void InfoTick()")[0]
        self.assertIn('"Thread border router"', build_info)
        self.assertNotIn("UsageSummary", build_info)
        self.assertNotIn("RecordLaunch", source)
        # the info timer must not run while another page or app is visible
        self.assertIn("infoTimer?.Stop();", source.split("void BuildPage()")[1].split("Add(new ImageView")[0])
        self.assertIn("if(page!=3)return false;", source)
        self.assertIn("clockTimer?.Stop();infoTimer?.Stop();", source)

    def test_info_page_lists_match_applied_privacy_state(self):
        info = (SOURCE.parent / "InfoStats.cs").read_text()
        self.assertEqual(info.count('"com.samsung.tv.') + info.count('"org.tizen.') + info.count('"AQKO') + info.count('"HEPsq') + info.count('"pisa-service"') + info.count('"canalysis-daemon"') + info.count('"slive-provider-daemon"'), 34)
        self.assertNotIn("File.Delete", info.split("RecordLaunch")[0])
        # SMACK: marker files are unreadable to the app and other apps' /proc is hidden; never infer from those.
        self.assertNotIn("File.Exists(\"/opt/usr/share/systemd", info)
        self.assertNotIn("attr/current", info)
        self.assertIn("if (AppEnabled(selfId) != true) return -1;", info)
        home = SOURCE.read_text()
        self.assertIn('infoMarkers<0?"Unavailable"', home)
        self.assertIn('infoDisabled<0?"Unavailable"', home)

    def test_info_live_state_survives_card_select(self):
        home = SOURCE.read_text()
        build_info = home.split("void BuildInfo()")[1].split("void InfoTick()")[0]
        self.assertIn("bool fresh=Environment.TickCount64-lastNetAt>10000;", build_info)
        self.assertNotIn("lastNet=null;lastCpu=null;\n  InfoTick();", build_info)
        # every live row is populated at build time, never a placeholder until the next tick
        self.assertNotIn('"Since start",null)', build_info)
        self.assertIn('"Since start",TotalsText(InfoStats.NetDev()))', build_info)

    def test_oled_idle_dim(self):
        home = SOURCE.read_text()
        self.assertIn("const long IdleMs=5*60*1000;", home)
        # idle check only runs from the clock timer, which is stopped while Quiet Home is in the background
        self.assertIn("IdleTick();return true;", home)
        self.assertIn("protected override void OnPause(){base.OnPause();editing=\"\";CloseInputs(false);clockTimer?.Stop();", home)
        # first key press only wakes, never activates a tile
        on_key = home.split("void OnKey(")[1].split("try{")[0]
        self.assertIn("if(dimmed){Wake(true);", on_key)
        self.assertIn("Wake(false);", home.split("protected override void OnResume()")[1].split("\n")[0])
        dim = home.split("void Dim()")[1].split("void MoveDimClock()")[0]
        self.assertIn("infoTimer?.Stop();", dim)
        self.assertIn("new Color(0,0,0,1)", dim)

    def test_inputs_sheet(self):
        home = SOURCE.read_text()
        # every key is consumed by the open sheet before page navigation; Back closes it
        on_key = home.split("void OnKey(")[1].split("if(k==\"XF86Home\")")[0]
        self.assertIn("if(inputsLayer!=null&&HandleInputsKey(k))return;", on_key)
        sheet = home.split("bool HandleInputsKey(")[1].split("void CloseInputs(")[0]
        self.assertIn("SelectSource(s.Type)", sheet)
        self.assertIn("CloseInputs(true)", sheet)
        self.assertIn("CloseInputs(false);", home.split("protected override void OnPause()")[1].split("\n")[0])
        # read-only source API only: names come from the TV, never renamed from here
        info = (SOURCE.parent / "InfoStats.cs").read_text()
        self.assertIn("get_source_list()", info)
        self.assertNotIn("set_edit_name", info + home)
        # powerState is unreliable on this TV (the on-screen SHIELD reports Off), so it is never shown
        self.assertNotIn("s.Power==", home)
        self.assertNotIn("src.Power==", home)
        for g in ("glyph-inputs.svg", "glyph-ambient.svg", "glow-watch.svg", "glow-play.svg"):
            ET.parse(ASSETS / g)

    def test_dark_theme_background(self):
        self.assertIn("Window.Default.BackgroundColor=new Color(0,0,0,1);", SOURCE.read_text())
        self.assertIn('fill="#000"', (ASSETS / "free-background.svg").read_text())


if __name__ == "__main__":
    unittest.main()
