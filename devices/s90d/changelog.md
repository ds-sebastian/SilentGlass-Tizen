# SilentGlass Home - changelog

The launcher was called Quiet Home (the name inherited from Quiet Tizen) until the SilentGlass-Tizen rename; older
entries keep that name.

Newest first. Measurements are from the tested TV (firmware in [README.md](README.md)); `bench/` holds the raw
records. Conf keys mentioned in older entries may since have been removed - the current list is in the README.

- **v31a**: Home over the picture rebuilt the way Vetro does it: a separate notification-level window above the TV
  viewer, which stays in front and playing (v30's full-screen video backdrop was dropped - it re-routed the video and
  was unreliable). New design: one glass bar (inputs, Inputs, Controls, Apps, Info), Inputs as a menu anchored above
  its item, Apps as their own floating menu, Controls as a right-side panel with level rows; no shader rims over the
  picture; new SF Symbols-style icon set. Designed with 1:1 Chromium mockups (`tools/overlay_mockup.py`) before the
  TV. The full-screen window now hides whenever another app is in front.
- **v30c**: **Home over the picture** (tvOS 26 direction). With a picture on screen, Home no longer covers it: a Liquid
  Glass dock (inputs, Inputs, Controls, Info, optional apps row), a Control Center column on the right (brightness,
  contrast and volume as level tiles - the effect is visible on the picture as it changes), and Info over the
  wallpaper at 92%. Full-screen 1920x1080 `VideoCanvasView` behind a transparent window; verified on the TV that the
  plane stays full screen and source/signal never change. Contrast added to the settings. v30a/v30b were trials (a
  clipped Picture Mode value; the Inputs sheet overlapping the apps row).
- **v29b**: the Watch/Play cards show the TV's own input names (no more built-in "SHIELD" / "Nintendo Switch"), and
  re-read them on Home, on resume and every 30 s while Home is shown (renames fire no source-list event).
- **v28a**: accepts `quiet_source=<id>` app-control requests (from the Home Assistant bridge) and switches input exactly
  like a card press; only inputs the TV lists are accepted.
- **v27a**: chip temperature in This TV (`/sys/devices/platform/soc/thermal/temperature`, Samsung's driver file; these
  TVs have no Linux thermal zones); Privacy card counts the 13 batch E apps (39 in total).
- **v26a (SilentGlass-Tizen)**: the fork is renamed SilentGlass-Tizen and the launcher SilentGlass Home (window
  title, Info heading and About tile; on-TV file names unchanged). Network card shows a wired glyph on Ethernet.
- **v25b (Info overhaul)**: four status cards (Privacy / Network / Thread / This TV: coloured glyph disc + one word,
  details below, small sparkline in Network; not focusable) and a grid of Quiet Home settings: Apps tab, Live
  preview, Dim when idle, Wallpaper (Auto / Day / Night), Thread border router (where installed). Settings are
  saved in `quiet-home-prefs.txt` (app data dir, survives reboots); older files load with defaults. Info tiles
  grow 3% on focus (6% touched neighbours); no value is ever truncated (rates shown as one combined number).
- **v24a**: Info switch "Thread border router" (only where the switchable units are installed); the Thread panel
  now refreshes every 2 s, so it follows the daemons stopping and starting. See thread-border-router.md section 5.
- **v23a**: Info page switch "Show Apps tab" (own focus row under the panels; panels 370/330 tall to make room).
  Hidden = the tab bar is rebuilt without Apps and Left/Right skips it (`HomeLogic.NextTab`); saved as `hideapps=`
  in `quiet-home-prefs.txt`. TV apps stay installed; Samsung Home is only reachable from the Apps tab.
- **v22a (refactor)**: measured-and-dropped experiments removed (flat/`liquid=0` fallback, `glass=`/`shadow=` modes
  and BoxShadow, procedural `wall=shader`, live `still`/`sync`/`cover` modes; old `/tmp` conf keys are now ignored).
  Pure logic moved to NUI-free `HomeLogic.cs` (conf parsing, Watch/Play port rules, video rectangle, sunrise/sunset)
  and unit-tested off the TV in `tests/CatalogTests.cs`. Fixes: the saved Watch/Play input was never read back
  (loaded in a field initializer, before the app framework existed); cycling can no longer give both cards the same
  input; Select on the tab bar enters Home at the current input's card; the state file is built from one key list
  (duplicate `hero_ports` removed). Test helper `body()` now brace-matches (it used to run past one-line members).
- **v21d**: interrupted focus animations land on their end values (`EndAction=StopFinal`; plain Stop() froze a
  tile mid-fade, e.g. Inputs stayed highlighted after Menu paused the app), and every slot is restyled on resume.
- **v21c**: Home key while already on Home only refocuses (it used to rebuild the page: cards re-decoded and the
  live video re-attached = a flash after Inputs -> Home). The video plane gets a 4 px bleed behind the card art and
  its rectangle is re-asserted on signal events (a plane 1 px short of the see-through hole showed as a thin line).
  Focused non-live hero grows 2% (was 4.5%). Preview gaps with no signal are the TV's own: Samsung's launcher
  drops its preview the same way (SHIELD asleep -> mainscreen_signal_state 0).
- **v21**: live hero card gets an eased bottom band (clear -> 82% black over 330 px) instead of a word box - the
  video plane is separate hardware and cannot be blurred, only darkened. Shadows follow Apple's restraint: none on
  hero cards at rest (soft one only on focus), faint on tiles, light on the floating sheet/toast. Controls page is
  5x3: adds Watch Input / Play Input (Select cycles Automatic -> HDMI 1-4, saved in the app's data directory as
  `quiet-home-prefs.txt`; the card subtitle follows the TV's input name) and Pixel Shift (`pixel-shift-menu`, Panel
  Care). Pixel Refresh is deliberately not offered: the TV runs it by itself in standby (Samsung's own help text);
  the manual run (`DDDisplay.QDDisplayRequestELSensing`, platform privilege) is only for visible retention.
  `herofade=1` in the conf restores the translucent unfocused hero card.
- **Photo wallpaper, day/night (v20)**: macOS Catalina Day/Night (not in git - Apple artwork; build them with
  `tools/make_wallpapers.py DAY.jpg NIGHT.jpg`, Quiet Home falls back to the gradient wallpaper without them). Switches
  at local sunrise/sunset computed from UTC (time zone independent). The glass samples a pre-blurred 480x270 copy
  (frost texture) instead of evaluating the gradient formula: 47.6% GPU @60 fps vs 54.5% in v19c, GPU memory
  ~30 MB -> 15.9 MB (JPEG decodes to YUV planes; gradient SVG now rasterised at half size; no full-screen glow
  layers over the photo). Live hero card: smoked-glass plate behind the words, only while video plays. Unfocused
  heroes dim by colour, not opacity (a faded card shows the photo through). Inputs sheet lighter (less dim, thinner
  tint, softer blur). Conf: `wall=photo|gradient|shader`, `wallmode=auto|day|night`, `lat=`/`lon=`.
- **Liquid Glass + 60 fps (v19)**: one GLSL ES 1.0 shader (`Home.Glass.cs`, same dialect as Samsung FLUX) draws every
  glass surface: lensing of the backdrop near the rim with a colour split, a specular edge lit from the top left plus a
  glint on the opposite corner, thick-glass edge darkening, a top-lit body, an analytic soft shadow and dithering. The
  wallpaper is evaluated mathematically in the shader (no screen readback, no blur passes); focus glows are shared
  shader uniforms. Rims and shadows of big cards use a ring mesh (edge band only); panel insides take the backdrop from
  the vertices. Heroes: tvOS 26 icon-style art (vivid field + frosted glass symbol). DALi on this TV renders every
  2nd vsync by default; `NUIApplication.SetRenderRefreshRate(1)` gives 60 fps (`vsync=` in the conf file).
  Measured while redrawing every frame: flat 14% @30 / 35% @60; Liquid Glass first cut 83% @60, final 54.5% @60
  (59.6-60 fps); procedural wallpaper saturated the GPU (48 fps) and stays off. Key handling, page builds and RSS
  unchanged within noise; GPU memory +2.7 MB (bench/liquid-glass-20261025.json).
- **Apple look (v18)**: glass surfaces (top-lit tint + fine rim) on tiles, tab capsule and Info panels; real GPU
  backdrop blur (`RenderEffect.CreateBackgroundBlurEffect`) on the Inputs sheet and toast, which float over moving
  content; Apple continuous corners (`CornerSquareness`). Tunable at runtime in `/tmp/quiet-home-live.conf`
  (`glass=0/1/2`, `shadow=0/1`, `squircle=0/1`, `bench=N`). Measured (bench/apple-overhaul-20261025.json):
  GPU busy while redrawing every frame - flat 16-18%, glass+squircle 20%, +focus shadows 23%, +always-on soft
  shadows 27-28%, blur on every surface 25% (static backdrop is cached). 30 fps in every mode (this TV runs the app
  at 30 fps). RSS/PSS/GPU memory and key handling unchanged within noise, so shadows ship off.
