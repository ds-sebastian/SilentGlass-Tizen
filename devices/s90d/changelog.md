# SilentGlass Home - changelog

The launcher was called Quiet Home (the name inherited from Quiet Tizen) until the SilentGlass-Tizen rename; older
entries keep that name.

Newest first. Measurements are from the tested TV (firmware in [README.md](README.md)); `bench/` holds the raw
records of the 2026-10-25 style benchmarks. Conf keys mentioned in older entries may since have been removed - the current list is in the README.

- **v43a** + **bridge 2.8.0** (installed and checked on the TV 2026-10-27): one contract file
  (`launcher/src/Shared.cs`) compiled into both, for app ids, launch extras, the data-directory files and the input
  list; dead code removed. Fixes: the phone page could report "Connected" after saving settings that then failed (it
  read the old connection's state); a broker gone without a reset (network loss) was never noticed (now 90 s without
  a packet ends the connection); the setup sheet's auto-close timer could be collected before it fired; a setting
  changed on the TV made SilentGlass reload and rebuild the bar. Info shows "Connecting…" while the bridge tries new
  settings. Deploy pair: a staged file that failed its checksum no longer passes under `set -e`. Phone page: a wrong
  code clears the field, network failures are reported, accessible buttons and switches.
- **v42l**: a green dot on every input whose device is on, not just the one on screen. HDMI-CEC lists the devices
  that are awake (a sleeping one drops out); read in-process over GDBus (`Cec.cs`, ~50 ms) when the bar or Inputs
  opens, cleared on standby. A device plugged straight into the TV must also not report standby; behind a soundbar
  the port's answer is the soundbar's, so being listed counts. Measured: SHIELD on HDMI 3 behind the Q990D listed and
  dotted; the sleeping Switch not listed, no dot.
- **bridge 2.7.0**: "This TV" › Name is the TV's own name (Settings › Device Name; also phones, SmartThings,
  Bluetooth). Saving renames the TV with Samsung's own single write; Home Assistant's device follows renames made
  on the TV too (watched key). Entity ids for new installs come from the name, the model from the TV. Measured: a
  rename showed at once on the TV's port-8001 API and in Home Assistant.
- **bridge 2.6.0**: notification settings on the phone page (Advanced settings › Notifications), applied by the
  bridge to every notification: position, size and time on screen (each "As sent" by default), pictures on/off,
  quiet hours (dropped, not queued; TV time), and "Send a test notification" with the page's unsaved values (ignores
  the on/off switch and quiet hours, reports why it was not shown). Config keys `notify_position`, `notify_size`,
  `notify_duration`, `notify_images`, `notify_quiet`. Measured on the TV: test in standby -> "the TV is off"; with
  the picture on, a small banner at x=56, y=56 (top-left).
  - The installer's DLL now travels xz-packed (the TV has `/usr/bin/xz`): 58.9 KB, was 66.8 KB with gzip, over the
    TV runner's 64 KiB.
- **v42k** + **bridge 2.5.1**: no built-in location. Until one is set on the phone page, sunrise and sunset come from
  the TV's time zone (standard offset x 15 degrees of longitude, latitude 40). Examples and tests use New York.
- **bridge 2.5.0**: about a third of the CPU: ~1% of one core (137 ticks in 120 s, per-thread `/proc/<pid>/task/*/stat`,
  bridge 2.6.0 in standby), was ~3% (452 ticks in 150 s, picture on). An earlier "~0.4%, a seventh" figure came from
  a 60 s window read as 120 s (the TV runner does not pass environment variables such as `SECS=`).
  - Power, input and the app in front are published when they change (vconf change notifications on the bridge's
    GLib loop, plus the source list callback); measured: power on reached Home Assistant in 1.5 s.
  - Volume, mute and input names are checked every 10 s instead of 2 s, settable on the phone page (Advanced
    settings › Home Assistant: 2 s / 5 s / 10 s / 30 s / 1 min; `update=` in the config). Temperature, memory, Thread
    role and picture settings every minute (was 30 s).
  - The picture/sound mode lists (dozens of settings-service calls) are no longer read on every state check, only
    with the once-a-minute check.
  - The two idle wake-ups that cost more than the work: the setup-page watch polled for its request file every
    0.4 s (now inotify; the page also opens faster, 0.16 s), and the main loop sleeps until the next check, a change,
    a lost connection or new settings.
  - Log: per-minute counts of change notifications, when there were any.
- **v42g-v42j**: the bar no longer flashes over a picture, and every Home press counts.
  - Window traces (`enlightenment_info -topvwins` every ~29 ms) showed what Controls (never flashing) didn't do:
    each open *showed* the bar's window, and the compositor drew a newly shown window in software for ~70 ms before
    moving it to a hardware layer over the video - the double/triple flash. Over the wallpaper there is no video
    under it, so nothing showed. Now, over the picture, a closed bar's window is **parked**: emptied, unable to take
    focus (the remote goes back to the viewer), still on screen; it goes up parked whenever the viewer comes to the
    front, and is really hidden only when another app or standby takes over. Opening is a content change, like
    Controls. (The TV still hides its own OSD layer ~0.5 s after each Home launch; registering SilentGlass as a
    "partial" app in `.app-context.db` did not change that and was reverted.)
  - The 1.2 s window that dropped key-less Home requests after an opening press is gone: since the window stays up
    no repeated request has been seen, and fast real presses were being dropped. A request is dropped only if it
    repeats the TV's launch stamp (`__K_APP_LAUNCH_START_TIME`). SilentGlass's own relaunch (after going to the
    picture first) carries `sg_self`, so it is never taken for a press.
  - A cold launch (first Home after SilentGlass restarted) over the picture left Samsung's near-black launch
    placeholder in front, holding the remote. When SilentGlass is the front app with nothing full screen of its own,
    it now brings the viewer back after 0.4 s (the input re-syncs once) and the open bar takes the remote again.
- **Undo scripts tested offline**: every restore script was run in a sandbox on the Pi against copies of the tested
  TV's files (registry, settings, owner units, markers, launcher, all 6,328 backup files), one at a time and as one
  chain back to stock (45 disabled apps -> 1, the one the Thread undo re-enables; Home and startup back to Samsung's).
  Fixed what would have refused on the TV: `privacy/restore.sh` (Samsung had uninstalled one of its 19 apps; absent
  apps are now skipped, present ones must still match their recorded rows), `prime-restore.sh` (Samsung's Prime
  reinstall had put the boot metadata back; now skipped, the companion is still re-enabled). New
  `launcher-deploy/restore-stock-home.sh`: the TV's own trial rollback only accepted the first trial build.
  `tests/test_s90d_undo.py`.
- **v42f**: the settings-key bridge is gone (it replaced Samsung's QuickLaunch app; never installed on the tested
  TV): `QuickLaunch.cs`, its recipes and tests removed, and the launcher no longer reads its old `quiet_page` extra.
  `sg_open` stays for `app_launcher`. Deploy pair `update-v42f.sh` / `restore-v32a.sh`. Also removed from the TV: a
  leftover diagnostic unit `sg-probe.service` (never enabled; backed up to `sgprobe-leftover-20261027`).
- **v42e**: no wait before reopening the bar. The 2.6 s after-close window swallowed real presses: in one test
  session 6 Home presses in a row (0.3 s apart) were dropped after a close. Now closing starts no window. Only a
  launch request with no Home key, within 1.2 s of the press that opened the bar, is dropped (the TV's duplicate);
  a real press while the bar is up always reaches it as a key first, so it always counts. Opening, closing and
  reopening all within 1.2 s is the one case where a press can still be dropped. Stray requests after a close
  aren't filtered any more (the bar hides on the key's release since v42b, and the log showed none after a remote
  Home close); if they come back, the cost is a bounce, not a delay - the owner's preference. Also: Home pressed
  just after the picture dropped (counted as over it) now brings the wallpaper up behind the bar if the picture is
  still gone when the 4 s grace ends. Screen sim: "<step> quickly" steps, a 0.5 s "Home over the picture opens at
  once" rule (proven to fail on v42d), `SG_SIM_TRACE=<scenario>` prints one scenario's log. 
- **Release tooling**: `tools/build.py bridge` builds the bridge reproducibly (same flags as the launcher; the
  installer embeds that build), the SDK is pinned (`global.json`), `tools/release.py` builds the release files from
  a clean checkout and checks for local-only assets, and CI checks that the committed installer is its source. Lab
  paths removed from tests, bench helpers and the drift check (`TV_ACCESS` / `tv_access`). Bridge reinstalled
  from the reproducible build (same source as 2.4.1).
- **v42d** + **bridge 2.4.1** + **batch G**:
  - The phone's code screen still looked see-through: the phone had switched to its old tab (same address) instead
    of loading the new page. The QR now carries a fresh `?s=` per sheet (not the code), so phones load it anew. The
    page no longer reads a code from its address at all.
  - Apps updated with "Auto update apps" off: that menu setting (`db/apps/auto_update`) is read by
    `was-appupdate`, but `was-builtinupgrade` updates Samsung's built-in apps on its own key,
    `db/built-in-app-upgrader/server_upgrade` (was on). `cleanup/batchg-apply.sh` turns it off, and the OMA-DM
    firmware auto-update (`db/SyncML/ivi-oma-dm-service/software-update/autoupdate/setting`, separate from the
    menu's), with the old values saved; `batchg-restore.sh` puts them back (round trip tested). The drift check
    now verifies all four update keys.
- **v42c**: a double/triple flash when the bar appears. A window trace (`enlightenment_info -topvwins` every ~40 ms)
  shows a warm open is one clean step - overlay hidden -> shown, nothing else moves - so the flash is the overlay's
  own content. Likely cause: the window was hidden before it was cleared, so it kept the old bar as its last frame,
  and the compositor showed that frame again on the next Show before SilentGlass's first new frame (old bar at full
  opacity, then the new one fading in from nothing). Now the overlay is cleared, given 80 ms (~5 frames) to present
  the empty scene, then hidden; a reopen inside that time just cancels the hide. The main window ends the dim first
  and hides after 80 ms (it is black underneath, so it keeps the wallpaper rather than emptying).
- **v42b**: Home ignored for a few seconds after closing the bar. That was the 2.6 s guard against the TV's stray
  launch requests. Likely cause of the strays: the remote sends press and release separately; SilentGlass hid its
  windows on the press, so the release reached the TV with nothing of ours up, and the TV relaunched the Home app
  (Home Assistant's keys, pressed and released together, never caused them). Whatever a key hides now waits for that
  key's release (at most 1.5 s), and a launch request right after the Home key itself reached the bar is always a
  real press. The guard stays until the event log shows no strays (`released <key>` marks a deferred hide; launch
  requests log their extra keys).
- **v42a** + **bridge 2.4** + **re-apply**: Advanced settings.
  - Info: Home Assistant is a status line (Connected / Not set up / Can't connect / Not running); the last row,
    **Advanced settings**, opens the phone sheet (QR + code). The Thread border router switch moved to the phone.
  - The phone page is now Advanced settings: Home screen (Watch / Play inputs, Apps in the bar, Dim when idle,
    wallpaper and its location), Notifications, Thread border router (on/off + network), Home Assistant (MQTT), the
    TV's name. Home-screen settings go to SilentGlass's prefs file, which it reloads when it changes.
  - The code screen was drawn under the settings rows (no stacking order) - the page looked garbled; it now covers
    the page, which stays hidden until the code is accepted.
  - Privacy said aov-dashboard was on: a Samsung app update (22:53, v1.24.443) reinstalled it enabled. A registry
    edit was not enough - pkginfo-server kept the updated app's info cached as enabled - so `cleanup/reapply-apply.sh`
    re-disables anything on the privacy list through `pkgcmd -D` (the TV's own app disable), with a backup and
    record per run; `reapply-restore.sh` undoes the latest run with `pkgcmd -A`. Bridge installer regenerated (2.4).
- **v41g**: after the TV woke with SHIELD coming on through the soundbar, the screen stayed black while the TV
  reported SHIELD selected with a picture. Watch only closed the bar ("already watching"); Inputs > SHIELD - a real
  input selection - brought the picture up. Watch and Play now always select their input, exactly like Inputs. The
  setup QR code is now the address only, so the phone always asks for the 6-digit code shown on the TV (it used to
  carry the code). All launch requests, real presses and the TV's strays, come from `tvctx-manager`, so the caller
  cannot tell them apart; the 2.6 s window stays.
- **v41f**: the bounce, again; clearer current input.
  - v41e's event log showed the rest of the bounce: after the bar closes on a remote key (Back, Watch, Home), the TV
    sends the Home app one or two more launch requests 0.7-2.2 s later, with no key, and the second reopened the bar.
    A plain Home within 2.6 s of the bar closing is now ignored (and within 1.2 s of the last Home, as before). Cost: a
    real Home pressed within 2.6 s of closing the bar is ignored too. Every launch request now logs its caller
    (`request from ...`), to tell the TV's strays from real presses.
  - The TV re-sends the same signal value whenever SilentGlass steps aside; each one rebuilt and re-faded an open
    Inputs menu. Unchanged values are now dropped, and Inputs redraws only when its contents change (no fade).
  - Things appear by fading in place (180 ms), with the dimming behind the bar fading too; the 16-24 px rise is gone.
  - The input on screen keeps a soft pill, on the bar (Watch/Play) and in Inputs (with its checkmark); the focus is
    the white pill; the green dot means that input has a picture.
- **v41e**: the bounce. The event log showed that the remote's Home reaches the app twice: a second launch request
  0.4-1 s after the first undid it, so the bar closed and reopened on every press (and reopened about 1 s after
  Watch). A plain Home within 1.2 s of the last one, or of the bar closing, is now ignored (`launch:echo` in the log).
  When the picture arrives under the wallpaper while the bar is open, the bar now stays, over the picture, instead of
  closing. `tests/screen-sim` models the double Home; its Home-press checks had never fired (a Wait step cancelled
  them) and now do: without the fix, about 570 of 1,808 runs broke them; with it none of 11,408.
- **v41c/v41d** + **batch F2**: Info read "Check · 53 of 54" while the registry, the app framework and the drift check
  all said everything was blocked. Info now names what is on instead of a count (and the state file lists it as
  `privacy_gaps=`, checked 8 s after start). It named Apple TV: its package also holds `com.samsung.tv.aria-dummy`,
  which batch F missed (Prime Video's dummy was disabled with it). Batch F2 disables it; Info and the drift check
  list it.
- **v41b**: from the first evening of use.
  - TV on with SHIELD off showed the wallpaper (right), but switching SHIELD on left it up: the TV did not report
    the signal flag change while the wallpaper was in front. While the wallpaper is up, the flag is now read every
    second as well (nothing runs otherwise). `tests/screen-sim` gained that TV behaviour as a variant and the case as a
    scenario (1 s passes the 2 s rule; 2 s did not).
  - "The launcher bounces when it appears": the TV's compositor effect was still on for the app's main window (the
    one that hides and shows on Home presses); now off, as for the overlay.
  - `/tmp/silentglass-events.log`: every event (keys, launches, signal, bar and wallpaper show/hide), for diagnosing
    what someone saw; the state file's trail holds only the last 16.
- **v41a**: the wallpaper no longer comes up over SHIELD. What is on screen is now decided in one NUI-free file,
  `launcher/src/Screen.cs`, and `tests/screen-sim` runs that exact code against a simulated TV (SHIELD and Switch
  sleeping, waking over CEC and resyncing, Ambient, standby, Home Assistant launches, idle close, OLED dim), under every
  combination of the TV behaviours not yet measured. Against v40i it found about 1,200 runs with the wallpaper over a
  live picture and about 160 with a bar the remote could not reach; v41a breaks no rule in 12,088 runs.
  - Home during a picture blip (SHIELD waking, an input still syncing, a frame-rate switch) used to bring up the
    wallpaper, which then never left - not when the picture came back, not on a second Home press, and after 5 min
    it dimmed to black over SHIELD. Now the picture arriving under the wallpaper takes the screen, the viewer
    coming to the front ends the wallpaper, and Home within 4 s of a dropout counts as over the picture.
  - Home from Ambient (the remote, or Home Assistant's Home button) went to the wallpaper even with SHIELD on and
    selected. It now goes to the picture first, waits for the signal flag (stale while Ambient runs) to settle, then
    opens the bar.
  - An input switch SilentGlass did not make (HDMI-CEC One Touch Play) and standby (`memory/pm/state`, which the
    app can watch) now close the bar and the wallpaper; before, a CEC switch left a bar the keys no longer reached,
    and a bar open at power-off came back dead at power-on.
  - Checked on the TV: cold start (bar, keys), Home with the Switch asleep (wallpaper), switching to SHIELD with Home
    pressed before its signal settled (the wallpaper stepped aside), Ambient -> Home (bar over SHIELD, keys work),
    standby with the bar open (closed itself).
- **v40i** + **batch F**: the drift check found Prime Video's companion enabled again - and Prime Video, Apple TV and
  the web browser reinstalled ~3.5 h after batch D uninstalled them (Samsung's country preloads come back; Prime was
  running in the background). Batch F keeps them installed but disabled, which sticks, as it has for TV YouTube;
  stopping them freed ~90 MB. Info's privacy check and the drift check now cover them. The drift check reads clean
  again. Installed; it takes over at the next Home
  press (the TV was put in standby before an on-screen check).
- **v40h** (+ HA bridge 2.3, installed and checked on the TV): first on-screen run of v40a, and the fixes it needed.
  - **Keys at cold start**: the TV's launch placeholder (`[WIND]com.samsung.tv.bixby-promotion`) holds the remote's
    keys until the app's main window has been shown once. v40a hid that window at start, so the bar appeared but
    never got a key. The main window now starts shown (black, empty) and Home hides it when the bar opens; the bar's
    window is created at start, as v31 did.
  - **The bar closed itself**: the TV pauses the app whenever only the overlay is up, and v40a closed the overlay on
    pause. Now only another app coming to the front closes it.
  - Info's network line drops "full duplex"; a phone page that times out returns the sheet to Info; a diagnostic
    event trail (`trail=`) in the state file; the Thread symbol is now Thread's ring-and-loop mark.
  - Checked on the TV: bar, Inputs (switches; unplugged inputs refused by the TV), Controls (brightness 50 -> 49 ->
    50, read back), Info (Protected, Wired 100 Mb/s, Thread router with home reachable), the setup sheet end to end
    from a "phone" (QR decodes; wrong code refused; broker test; Home Assistant's Thread network found; save and
    reconnect; the password never sent back; Back closes the page and port 7680), a Home Assistant notification
    over SHIELD, the Ambient -> Home redirect with the wallpaper backdrop, and input switching from Home Assistant.
- **v40a** (+ HA bridge 2.3): **the final shape - overlays only.** No full-screen pages any more: the Home, Apps,
  Controls and Info pages, the live preview and the glass shader are gone. Over a picture Home toggles the bar;
  Inputs and Apps open above it; Controls and Info are panels on the right that replace the bar (Info near-opaque,
  with all of SilentGlass's settings); with nothing to draw over, the app's window is only a wallpaper backdrop.
  Pixel Shift is a switch in Controls. **Phone setup**: Info > Home Assistant shows a QR code + 6-digit code; the
  bridge serves a setup page while it is open (MQTT with a live test, TV name, notifications, Thread from Home
  Assistant or a pasted dataset, wallpaper location) and installs unconfigured. The launcher source was rewritten
  into nine files (`SilentGlass.cs`, `Overlay.cs`, `Menus.cs`, `Panels.cs`, `Setup.cs`, `Notice.cs`, `Backdrop.cs`,
  `Tv.cs`, `Ui.cs`) plus NUI-free `Logic.cs` and `Qr.cs`; the bridge into `Bridge.cs`, `TvApi.cs`, `Notices.cs`,
  `Setup.cs`, `Mqtt.cs`, `Http.cs`. Launch extras are now `sg_open` / `sg_source` / `sg_notice` (older QuickLaunch
  builds' `quiet_page` still works). The U8000F-specific toolkit was removed; its Samsung findings are in
  [docs/samsung-tizen-notes.md](../../docs/samsung-tizen-notes.md).
- **v32a** (+ HA bridge 2.2): **notifications on any input**. The bridge accepts Home Assistant's *Notifications for
  Android TV* protocol (port 7676, from the broker host only) and hands each notification to SilentGlass Home, which
  shows a tvOS-style banner in its own non-focusable notification window (image, title, message; position, size and
  duration from the request). Never wakes the TV; dropped in Ambient or when switched off. A cold SilentGlass is
  started hidden for it. Also: Game Mode is a three-way select in Home Assistant; the app sensor no longer reports
  SilentGlass while it only shows a banner or nothing over the picture; Game Mode's cycle ignores the hidden-item flag.
- **v31c**: Game Mode is one three-way control (Automatic / Always / Never) on the TV's real setting
  (`game-mode-auto`, Off / On / Auto). The old on/off switch drove `game-mode`, which only mirrors whether Game Mode
  is engaged, so it disagreed with Auto. The full-screen Controls page drops the duplicate "Auto Game Mode" tile and
  gains Contrast.
- **v31b**: the bar's input items are **Watch** and **Play** again, with the device each opens as a second line; Watch
  and Play can be re-pointed from the Controls panel (Left/Right, both directions; `HomeLogic.NextPort` gained a
  direction). Controls rows tightened (88/64 px) to fit the new section.
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
