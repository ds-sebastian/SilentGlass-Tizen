"""S90D port configuration checks, not a substitute for on-TV rendering tests."""
import csv
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "launcher/src/Home.cs"
SRC = ROOT / "launcher/src"
PARTS = ("Home.cs", "Home.Controls.cs", "Home.Inputs.cs", "Home.Info.cs", "Home.Idle.cs", "Home.State.cs", "Home.Live.cs", "Home.Style.cs", "Home.Glass.cs", "Home.Wallpaper.cs")


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
        self.assertIn('Hero("Watch","hdmi:"+watchPort,"SHIELD"', src)
        self.assertIn('lastSource=15', src)
        # heroes follow the TV's own input names, falling back to the observed ports
        self.assertIn('int w=PortNamed("shield",15),p=PortNamed("switch",13);', src)

    def test_secondary_keeps_the_tvs_stored_label(self):
        self.assertIn('Hero("Play","hdmi:"+playPort,"Nintendo Switch"', launcher())

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
        # owner: "On now" only meant "selected" - badges now come from the TV's signal state
        self.assertNotIn("On now", src)
        # status is a dot, never words; "selected" is a separate checkmark in the Inputs list
        self.assertIn('if(type==lastSource)return signal==1?Dot.On:signal==0?Dot.Off:Dot.None;', src)
        inputs = (SRC / "Home.Inputs.cs").read_text()
        for word in ('"On"', '"Off"', '"Not connected"', '"Unplugged"'):
            self.assertNotIn(word, inputs)
        self.assertIn('if(cur){r.Check=new ImageView{ResourceUrl=Root+"glyph-check.svg"', inputs)
        ET.parse(ASSETS / "glyph-check.svg")
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

    def test_new_artwork_is_local_valid_svg(self):
        # Fixed locally authored SVGs only; no uploads, remote XML, or DTDs are accepted.
        cards = [f"feature-{c}-{w}.svg" for c in ("watch", "play") for w in ("wide", "narrow")]
        for name in cards + ["tile-samsung-home.svg", "free-background.svg"]:
            tree = ET.parse(ASSETS / name)
            self.assertTrue(tree.getroot().tag.endswith("svg"))
            self.assertNotIn("https://", (ASSETS / name).read_text())
        for name in cards:
            art = (ASSETS / name).read_text()
            self.assertNotIn("<text", art)
            self.assertNotIn("<image", art)  # drawn art, no embedded app logos
            # drawn at the size the card is shown at
            self.assertIn(f'width="{996 if "wide" in name else 684}" height="560"', art)

    def test_current_input_gets_the_wide_16x9_card(self):
        src = launcher()
        self.assertIn("const int HeroWide=996,HeroNarrow=684,HeroGap=40;", src)
        # the big word fits its line (was drawn as "..." on the TV)
        self.assertIn('var big=Label(word,50,h-223,w-100,150,100,white,"Display");big.Ellipsis=false;', src)
        self.assertEqual(996 * 9 // 16, 560)  # 996x560 is 16:9 to the pixel, so Cover() returns the card itself
        self.assertEqual(996 + 40 + 684, 1720)
        self.assertIn("bool PlayIsWide()=>lastSource==playPort;", src)
        # follows input changes, without a fade
        self.assertIn("if(RelayoutHeroes())return;", body(src, "void OnInputsChanged("))
        self.assertIn("BuildPage(false)", body(src, "bool RelayoutHeroes()"))

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
        self.assertEqual(info.count('"com.samsung.tv.') + info.count('"org.tizen.') + info.count('"AQKO') + info.count('"HEPsq') + info.count('"pisa-service"') + info.count('"canalysis-daemon"') + info.count('"slive-provider-daemon"'), 36)
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
        self.assertIn("CloseInputs(false);ReleaseLiveWindow();clockTimer?.Stop();", on_pause)
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

    def test_ambient_home_returns_to_quiet_home(self):
        src = launcher()
        fg = body(src, "void OnForegroundChanged()")
        self.assertIn('now=="com.samsung.tv.csfs"&&IsAmbient(before)', fg)
        self.assertIn('Launch("com.samsung.tv.bixby-promotion")', fg)
        # only the Ambient -> Samsung Home hand-off is redirected; opening Samsung Home from Apps still works
        self.assertEqual(src.count('Launch("com.samsung.tv.bixby-promotion")'), 1)

    def test_info_card_select_does_not_flash(self):
        self.assertIn('if(a=="info"){BuildPage(false);return;}', body(launcher(), "void Activate()"))

    def test_apple_style_defaults_and_costs(self):
        style = (SRC / "Home.Style.cs").read_text()
        # glass + squircle on, shadows off by default (measured: shadows were the only real GPU cost)
        self.assertIn('public int Glass=1,Bench=0,Vsync=1; public bool Shadow=false,Squircle=true,Liquid=true; public string Wall="photo",WallMode="auto";', style)
        # real backdrop blur only where the backdrop moves, unless glass=2
        self.assertIn("if(movingBackdrop||style.Glass==2)", style)
        src = launcher()
        self.assertIn('Glass(sheet,34,SheetW,400,true,', src)
        self.assertIn("Glass(t,34,600,68,true,", src)
        # squircles never turn circles or capsules into squares
        self.assertIn("radius*2<Math.Min(w,h)", style)
        # nothing style-related is read or built on the key path
        key = body(src, "void OnKey(")
        for word in ("ReadStyleConf", "GlassMap", "CreateBackgroundBlurEffect", "KeepRendering"):
            self.assertNotIn(word, key)
        # glass maps are cached, focus moves reuse them
        self.assertIn("glassMaps.TryGetValue", style)

    def test_liquid_glass_shader(self):
        glass = (SRC / "Home.Glass.cs").read_text()
        # GLSL ES 1.0 like Samsung FLUX; no screen readback or blur passes
        self.assertIn("gl_FragColor", glass)
        self.assertNotIn("FrameBuffer", glass)
        self.assertNotIn("RenderTask", glass)
        # the procedural wallpaper must match the SVG it stands in for
        bg = (ASSETS / "free-background.svg").read_text()
        for stop in ("#2B3F8F", "#0F4D3A", "#4A2366"):
            self.assertIn(stop, bg)
        self.assertIn("vec3(0.169,0.247,0.561),0.42", glass)
        # per-page renderers are released with the page; sheet/toast rims with their views
        src = launcher()
        self.assertIn("ReleasePageRenderers();", body(src, "void BuildPage("))
        self.assertIn("sheetRim?.Dispose();", body(src, "void CloseInputs("))
        # one shared quad geometry and one shared shader
        self.assertIn("if(glassQuad!=null)return glassQuad;", glass)
        self.assertIn("if(glassShader!=null)return glassShader;", glass)

    def test_photo_wallpaper_day_night_and_fallback(self):
        wall = (SRC / "Home.Wallpaper.cs").read_text()
        # missing images (they are not in git) fall back to the gradient wallpaper
        self.assertIn('File.Exists(Root+"wallpapers/wall-day.jpg")', wall)
        self.assertIn('ResourceUrl=Root+"free-background.svg",Size2D=new Size2D(1920,1080),DesiredWidth=960,DesiredHeight=540', wall)
        # sunrise/sunset from UTC, so the TV time zone cannot break it; wraps past UTC midnight
        self.assertIn("DateTime.UtcNow", wall)
        self.assertIn("rise<set?now>=rise&&now<set:now>=rise||now<set", wall)
        # one shared texture set: every glass surface switches at once
        self.assertIn("frostSet.SetTexture(0,tex);", wall)
        src = launcher()
        self.assertIn("CheckWallpaper();", body(src, "protected override void OnResume("))
        # no glow layers over the photo
        self.assertIn('if(wallpaper==null&&!(Liquid&&style.Wall=="shader")){glowWatch=Glow("watch");', src)
        glass = (SRC / "Home.Glass.cs").read_text()
        self.assertIn("else bg = texture2D(sFrost,vUV).rgb;", glass)
        self.assertIn("if(kind==0&&frostSet!=null)r.SetTextures(frostSet);", glass)

    def test_live_word_plate(self):
        src = launcher()
        # smoked plate behind the hero words, hidden until live video plays under them
        self.assertIn('var plate=new View{Name="word-plate"', src)
        self.assertIn("Opacity=0};", src[src.index('Name="word-plate"'):][:400])
        self.assertIn('pa.AnimateTo(hero.Plate,"Opacity",1f);', src)
        self.assertIn("if(was?.Plate!=null)was.Plate.Opacity=0;", body(src, "void HideLive("))
        # words above the plate
        self.assertIn("card.Add(plate);card.Add(big);card.Add(subLabel);", src)

    def test_live_preview_is_deferred_and_released(self):
        src = launcher()
        # never built during the page build itself: posted to after the frame
        self.assertIn("void ScheduleLive(){OnUi(UpdateLive);}", src)
        self.assertNotIn("UpdateLive()", body(src, "void BuildPage(").replace("ScheduleLive()", ""))
        # only for the current input, only with a picture, only while Quiet Home is visible and awake
        update = body(src, "void UpdateLive()")
        # tunables come from a file read only when the preview is rebuilt, never on the key path
        self.assertLess(update.index("liveConf=ReadLiveConf();"), update.index("TVWindowControlAPI"))
        self.assertNotIn("ReadLiveConf", body(src, "void OnKey("))
        # the live card does not grow or lift on focus (the video plane does not follow those animations)
        self.assertIn("bool still=IsLiveStill(s);scale=f&&!still?1.045f:1;lift=f&&!still?-6:0;", src)
        # crop to fill (16:9 plane covering the card) instead of squashing into the card's shape
        self.assertIn("canvas.DisableAutoUpdateVideoGeometry=liveConf.Cover;", update)
        self.assertLess(update.index("DisableAutoUpdateVideoGeometry"), update.index("canvas.SetPlayer("))
        self.assertIn("if(s.W*9>=s.H*16){int h=s.W*9/16;", src)
        # video directly above the artwork, below the words
        self.assertIn("var art=hero.Card.GetChildAt(0);", update)
        # the status dot must not flicker with the signal flag
        self.assertIn("signalTimer=new Tizen.NUI.Timer(800)", src)
        self.assertIn('page==0&&visible&&!dimmed&&CurrentHasSignal', update)
        self.assertIn('s.Action=="hdmi:"+lastSource', update)
        self.assertIn("catch(Exception ex){liveState=\"failed:\"", update)
        # released whenever Home is left: pause, OLED dim, page rebuild
        self.assertIn("ReleaseLiveWindow();", body(src, "protected override void OnPause()"))
        self.assertIn("HideLive();", src.split("void Dim()")[1].split("void MoveDimClock()")[0])
        self.assertIn("HideLive();", body(src, "void BuildPage("))
        # the TV window hint is only turned off when Home is left - toggling it per preview re-synced the input
        self.assertNotIn('SetAuxiliaryHintValue(tvsHint,"0")', body(src, "void HideLive()"))
        self.assertIn('SetAuxiliaryHintValue(tvsHint,"0")', body(src, "void ReleaseLiveWindow()"))

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
        self.assertIn("win.Add(BuildWallpaper());", body(src, "void BuildChrome()"))
        self.assertIn('"free-background.svg"', body(src, "View BuildWallpaper()"))
        self.assertNotIn("BuildChrome()", body(src, "void BuildPage("))
        # page content is the only thing disposed on a page change
        self.assertIn("content.Remove(v);v.Dispose();", body(src, "void BuildPage("))

    def test_dark_theme_background(self):
        self.assertIn("Window.Default.BackgroundColor=new Color(0,0,0,1);", launcher())
        self.assertIn('fill="#000"', (ASSETS / "free-background.svg").read_text())


if __name__ == "__main__":
    unittest.main()
