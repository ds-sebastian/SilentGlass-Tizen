"""S90D port configuration checks, not a substitute for on-TV rendering tests."""
import csv
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "launcher/src/Home.cs"
SRC = ROOT / "launcher/src"
PARTS = ("Home.cs", "Home.Controls.cs", "Home.Inputs.cs", "Home.Info.cs", "Home.Idle.cs", "Home.State.cs", "Home.Live.cs",
         "Home.Style.cs", "Home.Glass.cs", "Home.Wallpaper.cs", "Home.Prefs.cs", "HomeLogic.cs")


def launcher():
    return "\n".join((SRC / p).read_text() for p in PARTS)


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


class S90dPortTests(unittest.TestCase):
    def test_all_partials_are_compiled(self):
        proj = (SRC / "Home.csproj").read_text()
        for p in PARTS:
            self.assertIn(f'<Compile Include="{p}" />', proj)
        # the NUI-free logic is also compiled into the off-TV C# tests
        self.assertIn('<Compile Include="../launcher/src/HomeLogic.cs" />', (ROOT / "tests/CatalogTests.csproj").read_text())
        self.assertNotIn("Tizen.", (SRC / "HomeLogic.cs").read_text())

    def test_body_helper_stops_at_the_member(self):
        src = "class C\n{\n void A(){x(\"}\"); // the TV's }\n }\n void B(){y();}\n}\n"
        self.assertEqual(body(src, "void A("), 'void A(){x("}"); // the TV\'s }\n }')
        self.assertEqual(body(launcher(), "protected override void OnResume()").count("\n"), 0)

    def test_primary_shield_uses_observed_source(self):
        src = launcher()
        self.assertIn('Hero("Watch","hdmi:"+watchPort,HeroSubtitle(watchPort,"shield","SHIELD")', src)
        self.assertIn('lastSource=15', src)
        # heroes follow the TV's own input names, falling back to the observed ports
        self.assertIn('int w=prefs.WatchPort!=0?prefs.WatchPort:PortNamed("shield",15),p=prefs.PlayPort!=0?prefs.PlayPort:PortNamed("switch",13);', src)

    def test_secondary_keeps_the_tvs_stored_label(self):
        self.assertIn('Hero("Play","hdmi:"+playPort,HeroSubtitle(playPort,"switch","Nintendo Switch")', launcher())

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
        tiles = re.findall(r'SettingsTile\("([^"]+)","([^"]+)","([a-z]+)",(\d),(\d)\)', launcher())
        self.assertEqual(len(tiles), 15)
        self.assertEqual(sorted((int(c), int(r)) for *_, c, r in tiles), [(c, r) for c in range(5) for r in range(3)])
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
        self.assertIn('Nav={"Home","Apps","Controls","Info"}', src)
        self.assertIn("int next=HomeLogic.NextTab(page,d,prefs.HideApps);", src)
        self.assertNotIn("Toolbar(", src)
        build_info = src.split("void BuildInfo()")[1].split("void InfoTick()")[0]
        self.assertIn('"Thread border router"', build_info)
        self.assertNotIn("UsageSummary", src + (SRC / "InfoStats.cs").read_text())
        self.assertNotIn("RecordLaunch", src + (SRC / "InfoStats.cs").read_text())
        # the info timer must not run while another page or app is visible
        self.assertIn("infoTimer?.Stop();", body(src, "void BuildPage("))
        self.assertIn("if(page!=3)return false;", src)
        self.assertIn("clockTimer?.Stop();infoTimer?.Stop();", body(src, "protected override void OnPause()"))

    def test_info_page_lists_match_applied_privacy_state(self):
        info = (SRC / "InfoStats.cs").read_text()
        self.assertEqual(info.count('"com.samsung.tv.') + info.count('"org.tizen.') + info.count('"AQKO') + info.count('"HEPsq') + info.count('"pisa-service"') + info.count('"canalysis-daemon"') + info.count('"slive-provider-daemon"'), 36)
        self.assertNotIn("File.Delete", info)
        self.assertNotIn("File.Write", info)  # Info is read-only
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
        self.assertIn('"Since start",TotalsText(netdev))', build_info)
        self.assertEqual(build_info.count("InfoStats.NetDev()"), 1)  # one read of /proc/net/dev per build

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
        self.assertIn('Squircle=HomeLogic.Flag(c,"squircle",true)', style)
        self.assertIn('Vsync=HomeLogic.Int(c,"vsync",1,1,4)', style)  # 60 fps by default
        # measured-and-dropped experiments stay dropped: flat fallback, BoxShadow, procedural wallpaper
        src = launcher()
        for gone in ("BoxShadow", "ProceduralWallpaper", "style.Liquid", "style.Glass", '"shader"', "FocusRing"):
            self.assertNotIn(gone, src)
        # real backdrop blur only on the floating sheet and toast (their backdrop moves)
        self.assertEqual(src.count("Frosted("), 3)  # definition, sheet, toast
        self.assertIn("Frosted(sheet,34,SheetW,400);", src)
        self.assertIn("Frosted(t,34,600,68);", src)
        # squircles never turn circles or capsules into squares
        self.assertIn("radius*2<Math.Min(w,h)", style)
        # nothing style-related is read or built on the key path
        key = body(src, "void OnKey(")
        for word in ("ReadStyleConf", "Frosted", "CreateBackgroundBlurEffect", "KeepRendering"):
            self.assertNotIn(word, key)

    def test_liquid_glass_shader(self):
        glass = (SRC / "Home.Glass.cs").read_text()
        # GLSL ES 1.0 like Samsung FLUX; no screen readback or blur passes
        self.assertIn("gl_FragColor", glass)
        self.assertNotIn("FrameBuffer", glass)
        self.assertNotIn("RenderTask", glass)
        # the gradient backdrop formula must match the SVG wallpaper it stands in for
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
        # sunrise/sunset from UTC, so the TV time zone cannot break it (behaviour tested in tests/CatalogTests.cs)
        self.assertIn("HomeLogic.IsNight(DateTime.UtcNow,style.Lat,style.Lon)", wall)
        # one shared texture set: every glass surface switches at once
        self.assertIn("frostSet.SetTexture(0,tex);", wall)
        src = launcher()
        self.assertIn("CheckWallpaper();", body(src, "protected override void OnResume("))
        # no glow layers over the photo
        self.assertIn('if(wallpaper==null){glowWatch=Glow("watch");', src)
        self.assertIn("if(glassShader==null||wallpaper!=null)return;", body(src, "void GlowTo("))
        glass = (SRC / "Home.Glass.cs").read_text()
        self.assertIn("else bg = texture2D(sFrost,vUV).rgb;", glass)
        self.assertIn("if(kind==0&&frostSet!=null)r.SetTextures(frostSet);", glass)

    def test_interrupted_focus_animation_lands_on_final_state(self):
        src = launcher()
        self.assertIn("a.EndAction=Animation.EndActions.StopFinal;a.Stop();", src)
        self.assertIn("styledAll=false;UpdateFocus(false);", body(src, "protected override void OnResume("))

    def test_home_key_on_home_does_not_rebuild(self):
        src = launcher()
        home = body(src, "void Home(")
        # on Home already: refocus only (rebuilding flashed the cards and re-attached the video)
        self.assertIn("if(page==0){", home)
        self.assertIn("if(!RelayoutHeroes()){UpdateHeroBadges(false);UpdateFocus();ScheduleLive();}", home)
        self.assertLess(home.index("return;"), home.index("BuildPage(false)"))
        live = (SRC / "Home.Live.cs").read_text()
        self.assertIn("var rect=HomeLogic.VideoRect(hero.X,hero.Y,hero.W,hero.H,4);", live)
        self.assertIn("if(liveView!=null&&liveSlot==hero){PushLiveGeometry(liveRect);", live)

    def test_live_band_and_light_shadows(self):
        src = launcher()
        # eased bottom band behind the words, only on the live card (no separate word box)
        self.assertNotIn("word-plate", src)
        self.assertIn("Scrim=true", src)
        self.assertIn("(0f,0f),(.25f,.18f),(.5f,.48f),(.75f,.70f),(1f,.82f)", src)
        # heroes: no shadow at rest, a soft one only when focused
        self.assertIn("Rim=RimOverlay(card,40,0,14,30,focusShadow:.26f)", src)
        glass = (SRC / "Home.Glass.cs").read_text()
        self.assertIn("float shadow=.12f,float shadowY=6,float blur=18,float focusShadow=.16f,", glass)

    def test_hero_inputs_are_user_choosable_and_persist(self):
        src = launcher()
        prefs = (SRC / "Home.Prefs.cs").read_text()
        self.assertIn("DirectoryInfo.Data", prefs)
        # read in OnCreate: in a field initializer the app framework (and its data directory) does not exist yet,
        # so the saved choice was never read back
        self.assertNotRegex(prefs, r"prefs\s*=\s*LoadPrefs\(\)")
        self.assertIn("prefs=LoadPrefs();", body(src, "protected override void OnCreate()"))
        create = body(src, "protected override void OnCreate()")
        self.assertLess(create.index("prefs=LoadPrefs();"), create.index("ResolveHeroPorts();"))
        # cycling never maps both cards to the same input (behaviour tested in tests/CatalogTests.cs)
        self.assertIn("HomeLogic.NextPort(prefs.WatchPort,playPort)", prefs)
        self.assertIn("HomeLogic.NextPort(prefs.PlayPort,watchPort)", prefs)
        self.assertIn('SettingsTile("Watch Input","heroport:watch"', src)
        self.assertIn('SettingsTile("Play Input","heroport:play"', src)
        self.assertIn("prefs.WatchPort!=0?prefs.WatchPort:PortNamed(\"shield\",15)", src)
        # Panel Care: Pixel Shift is a real setting; Pixel Refresh is never triggered from here
        self.assertIn('"cycle:pixel-shift-menu"', src)
        self.assertNotIn("ELSensing", src)

    def test_apps_tab_can_be_hidden_from_info(self):
        src = launcher()
        # switch row on Info (its own focus row below the panels); saved with the Watch/Play choice
        self.assertIn("AppsTabSwitch(100,900);", body(src, "void BuildInfo()"))
        self.assertIn('Action="toggle:apps",Row=2', src)
        self.assertIn('if(a=="toggle:apps"){ToggleAppsTab();return;}', body(src, "void Activate()"))
        self.assertIn("SavePrefs();BuildTabs();", body(src, "void ToggleAppsTab()"))
        # the tab bar is rebuilt inside its own layer, so it stays under the sheet, toast and OLED dim
        self.assertIn("tabBar=new View{Size2D=new Size2D(1920,120)};win.Add(tabBar);BuildTabs();", body(src, "void BuildChrome()"))
        self.assertNotIn("win.Add", body(src, "void BuildTabs()"))
        self.assertIn("if(tabs[i]==null)continue;", body(src, "void StyleTabs()"))
        # panels shrank to make room: the switch row ends above the notice line
        self.assertIn("InfoPanel(970,540,850,330,", src)
        self.assertLessEqual(900 + 72, 1000)

    def test_tab_bar_select_enters_at_the_current_input(self):
        key = body(launcher(), "void HandleKey(")
        self.assertIn("if(area==0||area==TB){area=1;selected=page==0?SourceIndex():0;", key)
        self.assertIn("if(selected<0||selected>=slots.Count)return;", body(launcher(), "void Activate()"))

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
        self.assertIn("bool live=IsLive(s);scale=f&&!live?1.02f:1;lift=f&&!live?-3:0;", src)
        # crop to fill (16:9 plane covering the card) instead of squashing into the card's shape
        self.assertIn("canvas.DisableAutoUpdateVideoGeometry=true;", update)
        self.assertLess(update.index("DisableAutoUpdateVideoGeometry"), update.index("canvas.SetPlayer("))
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
