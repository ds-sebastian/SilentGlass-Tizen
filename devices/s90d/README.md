# Samsung QN65S90DAFXZA (S90D, 2024 OLED)

This fork's working target. Owner setup: **SHIELD TV Pro** (via the Q990D soundbar, HDMI 3) is the main
device, **Nintendo Switch** on HDMI 1; the TV's own apps are backups. The phases below carry backups,
guarded inverses and hash checks. Recorded reboot checks apply to their tested phase/version;
newer edits still need their own acceptance rather than inheriting a blanket reboot guarantee.

- Firmware: `T-PTMDAKUC-0090-REL-202605141701` (system build string `TIZEN-TRUNK2025-PontusM-OSU-2024-HOTFIX-RELEASE_20260514.3`)
- Admin access: per-command UID-0 (root) sessions through the separately reviewed external
  `s90c-tools` `tv-access` helper, not the U8000F-gated `tools/tv.py` bundled here.
  Scripts in this device workflow are fed to `/bin/sh` on **stdin**; direct unsigned-script
  execution is restricted by UEP. The helper automates acquisition per command; it does not
  install permanent root or remove SMACK restrictions.

## Agent reading order and evidence scope

Start with the [agent walkthrough](../../docs/10-agent-workflow.md), then the relevant
scripts and inverse below. Read [access/persistence](../../docs/11-access-and-persistence.md)
and [upstream notes](../../docs/12-upstream-notes.md) before proposing another boot mechanism.
The [original access guide](../../docs/02-access.md) documents the U8000F track, not
an S90D installation command. Phase scripts and benchmark records are dated evidence,
not proof that every historical phase remains active or passed every boot scenario.
Re-read current device state and exact hashes before acting; never run every saved
installer merely because it exists.


## Quiet Home on the S90D (v21b)

Native NUI app installed in place of `com.samsung.tv.bixby-promotion` and set as the Home handler
(`db/appfw/home_appid`); power-on still goes to the last input (`startup_appid=org.tizen.tv-viewer`).

- **Home**: two hero cards - *Watch* (SHIELD) and *Play* (Switch) - plus Inputs, Ambient, Sleep Timer, Sound, More.
  Heroes bind to the inputs the TV names `Shield` / `Switch` (fallback HDMI 3 / HDMI 1), so re-cabling needs no change.
- **Live inputs**: `libsource-api` `register_sourcelist_changed_callback` + vconf `memory/system/source_type`
  notifications drive the *On now* / *Unplugged* badges, the Inputs sheet and a short "connected" toast. No polling.
- **Status dots, no words**: green = the current input has a picture, grey = no signal
  (`memory/tv-viewer/mainscreen_signal_state`, debounced 800 ms), empty ring = unplugged; other inputs get no dot
  (CEC reports power per TV port only, port 3 answers as the soundbar; the Switch dock is not on CEC). The Inputs
  list marks the selected input with a separate checkmark.
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
- **Hero cards follow the input on screen**: Watch stays left and Play right, but the current input's card is the
  wide one at exactly 16:9 (996x560), so its preview is uncropped; the other is 684 wide. Each artwork exists at
  both widths (`feature-{watch,play}-{wide,narrow}.svg`). The 100px hero word needs a 150px-tall label (116 drew "...").
- **Live preview**: the current input plays inside its hero card when it has a signal. FLUX `VideoCanvasView` +
  `TVWindowControlAPI` (Channel List's mini-TV recipe). Lessons from getting it right on the S90D:
  - the video plane does not follow scale/position animations, so the live card does not grow/lift on focus;
  - set `wm.tvs.win.use.tvs=1` + window transparency once and keep it until Home is paused - toggling it per
    preview made the TV re-sync the input, flapping the signal flag in a rebuild loop;
  - the canvas must sit directly above the card artwork (below the words), with a soft gradient for legibility;
  - crop, don't squash: the canvas (hole) is the card, but `DisableAutoUpdateVideoGeometry` +
    `ForceUpdateVideoGeometry(x,y,w,h)` give the plane a covering 16:9 rect; overflow hides behind the opaque UI and
    the HW clips the off-screen part with a matching source crop (seen: src 3423x2160 -> dst 887x560);
  - the TV can leave the main video muted while tv-viewer is backgrounded (black but correctly placed plane after
    Quiet Home restarted mid-preview): `IAVControl.MuteVideo(0)` on attach, as Channel List does.
  Verify without looking: `enlightenment_info -hwc_wins` lists the `VD` plane's `dst_pos` (e.g. `1100x0560+0100+0170`
  for the Watch card); `enlightenment_info -dump topvwins DIR` dumps the UI layer (the hole is alpha 0; video is not
  captured). Tunables in `/tmp/quiet-home-live.conf` (`enabled`, `still`, `sync`, `scrim` = 0/1).
- **Inputs sheet**: HDMI 1-4 + Live TV by the TV's edit name > detected name > port; *On now* / *Not connected*.
  `powerState` is deliberately not shown: on this TV the on-screen SHIELD reports Off (devices behind the soundbar,
  empty CEC device list).
- **Ambient**: one press to `org.tizen.art-app`. The Art app grabs Home and always opens Samsung Home
  (`ReturnType.Home` -> CSFS, hard-coded); Quiet Home watches `memory/appfw/current_fullscreen_appid` and brings
  itself back on that one hand-off.
- **Apps / Controls / Info** tabs; Info shows privacy state, network, live traffic and the Thread border router.
- OLED care: 5-minute idle fade to black with a drifting dim clock.

Source: `launcher/src/Home*.cs` (partials: Controls, Inputs, Info, Idle, State). Latency rules are enforced by
tests: no IPC/file I/O on the key path, incremental focus restyling, chrome built once, coalesced state writes.
Measured on the TV: key handling avg ~20 ms, source-list read 1.2 ms, warm page build ~60 ms.

Deploy: `launcher-deploy/update-vN.sh` (refuses unless the installed DLL is the previous version; keeps SMACK labels;
verifies every file) and `restore-v(N-1).sh`. Build with `python3 tools/build.py launcher` + TV framework DLLs.

## Device changes (device/)

| Area | What | Inverse |
|---|---|---|
| privacy/ | Phase B: `app_disable` on ACR, voice/Bixby/Alexa stacks, TV Plus (registry SQL + `AppMetadata` helper reload) | restore.sh / restore.sql |
| cleanup/batchc | ad targeting (`targeted-ad-service`), voice leftovers (`utterance-recommender-core`, `AICompanionAgent`, `aov-dashboard`) | batchc-restore.sh |
| cleanup/prime* | Prime Video boot metadata removed, companion disabled, `disable.appbooting` (ineffective, kept) | prime-restore.sh, prime-noboot-restore.sh |
| boot-units/quiet-recog-off | owner user-systemd watcher stopping content recognition (later boot targets restart it) | recog-boot-restore.sh |
| cleanup/batchd | SHIELD does everything: Prime Video, Apple TV, web browser **uninstalled**; TV YouTube and Samsung Home media rows disabled (cast to SHIELD's Chromecast; AirPlay unaffected); janitor unit removed | batchd-restore.sh (store apps: reinstall from Samsung Apps) |
| boot-units/quiet-prime-off | *removed by batch D* - janitor that closed Prime/Apple TV/browser in the background | batchd-restore.sh |

Install the units with `*-install.sh`, then the latest `*-update*.sh` (janitor: update5 then update5c). UEP also blocks `sh -n file`; check syntax with `sh -n < file`. User units live in
`/opt/usr/home/owner/.config/systemd/user` (label `User::Home`) and are driven with
`DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 systemctl --user`.

Not done on purpose: TV YouTube (`cobalt-yt`) is relaunched by litewebappservice within seconds when closed;
Samsung Home (`csfs`, ~100-120 MB) and its media rows (`home.media`, ~70-150 MB) are relaunched within ~1 min (measured), so closing them only burns CPU;
SmartThings/OTBR components are kept (custom Thread border router runs on `wpan0`); Art/Ambient services kept.

## Pi side (pi/)

`s90d_drift_check.py` + `s90d_expected.json` (set `tv`): weekly read-only check of firmware, Home/startup apps,
vendor markers, disabled apps, Quiet Home DLL hash, boot units and recognition. Prints nothing when all is well
(cron watchdog pattern); reports only after 3 consecutive unreachable runs.

## Findings worth knowing

- Settings API takes short menuitem ids from `settings-datafw/jsons/tv/*.json`; long catalog paths do not resolve.
- `get_source_list()` returns a malloc'd JSON string - bind it as `string` so the marshaller frees it.
- Google DNS `8.8.8.8` is a connmand fallback; a router "force local DNS" rule redirects it, no TV change needed.
- Soundbar input flips (TV eARC <-> HDMI 1) when leaving/entering SHIELD are CEC "active source" changes caused by
  the soundbar pass-through wiring; SHIELD straight into the TV (soundbar on eARC only) avoids them.
