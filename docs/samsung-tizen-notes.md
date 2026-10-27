# Samsung Tizen TV internals: what we learned

Field notes from two owner-authorized TVs - a 2025 U8000F (Tizen 9.0, ARMv7, .NET 6.0.9; the original Quiet Tizen
work) and the 2024 QN65S90D this project targets. Package names, units and keys below were observed on those
firmwares; a firmware update or another model can rename, re-enable or remove any of them. Verify on your own TV
before acting, and keep an inverse for every change.

## App registry and disabling things

- Apps live in the package-manager registry `/opt/dbspace/.pkgmgr_parser.db` (`package_app_info`). A package can
  hold several app ids; disabling an app record is not uninstalling its package.
- The reliable switch is three fields on the app row: `app_disable`, `app_onboot`, `app_autorestart`. The normal
  activation commands did not reliably report or keep the state on these builds.
- After changing rows, refresh the metadata caches (the S90D scripts in `devices/s90d/device/cleanup/` do this) and
  notify `memory/appfw/app_context_db_change` when key-filter rules change.
- Never disable display, HDMI, audio, network, Bluetooth, app-manager or accessibility services. Keep
  `org.tizen.menu` (advanced settings, Bluetooth pairing) and the container hosting the launcher
  (`com.samsung.tv.bixby-promotion`).
- Samsung catalog refreshes can restore cached and live preload flags and re-enable apps. Recheck after restarts and
  updates. Don't add a background loop that silently reapplies changes.

## Service groups

These groups come from the U8000F profile; the S90D batches (`devices/s90d/device/cleanup/`) cover the same ground
plus model-specific extras. Package names describe purpose; none of this is a traffic audit, and several entries
are ordinary optional features rather than trackers.

- **Advertising and viewing analysis** (recommendations and ads change):
  - `com.samsung.tizen.samsung-analytics`
  - `com.samsung.tv.acr`, `com.samsung.tv.canalysis-service`, `com.samsung.tv.contents-recognition-dbmgr`
  - `com.samsung.tv.adagent-service`
  - `com.samsung.tv.pisa-service`, `org.tizen.pisa-control-service`
  - `com.samsung.tv.targeted-ad-service`
  - `yL49PNFmjW.PromotionApp`, `yL49PNFmjW.service`
- **Voice** (voice control stops working):
  - `com.samsung.tv.alexa-client`, `-fullscreen-app`
  - `com.samsung.tv.bixby-context-client`, `bixby-prov`, `bixbycapsuleviewer`, `mybixby`, `ondevice-voice`
  - `com.samsung.tv.utterance-recommender-app`, `-core`
  - `com.samsung.tv.voice-instant-app`, `voiceassistant-music`
  - `com.samsung.tv.wakeup-engine-alexa`, `wakeup-engine-bixby`
  - `org.tizen.voice-app`, `voice-app-service`, `voice-client`, `voice-onboarding`
- **AI and context** (related smart features stop):
  - `com.samsung.tv.AICompanionAgent`, `AICompanionUI`, `AICompanionUILauncher`, `AICompanionUINotice`
  - `com.samsung.tv.ai-discovery-service`, `-ui`
  - `com.samsung.tv.context-aware-agent`, `-agent-toast-view`, `-grab-control`, `-server-requester`, `-service`
  - `com.samsung.tv.vif`, `content-grab-service`
- **Smart extras** (some integrations go away):
  - `com.samsung.tizen.smartthings-client`
  - `com.samsung.tv.adplayer`, `ambient-sound`, `aov-dashboard`
  - `com.samsung.tv.ghdaemon`, `ghservice` (Gaming Hub)
  - `com.samsung.tv.health-mde-service`, `-view`, `samsunghealth-service-tv`
  - `com.samsung.tv.smartthingsfind-daemon`, `-ondemand`
  - On the S90D, keep `com.samsung.tizen.smartthings-hub`: it holds the Thread radio stack (`ot-ctl`) the border
    router uses.
- **Light shell**: `com.samsung.tv.home.sidebar`, `org.tizen.browser`. Only after a replacement Home and settings
  path work.
- **Samsung TV Plus**: `HEPsqFNie0.tvplusstandalone`. It was started at boot by `org.tizen.litewebappservice` with
  `backgroundAtStartup`; the `slive-provider-daemon` stop marker keeps its provider down.

## Stopping vendor services

- Many vendor units carry a negated `ConditionPathExists` on a marker under `/opt/usr/share/systemd/...`. Creating
  the exact marker keeps the unit from starting at boot; deleting it restores the unit.
- Always verify the unit's actual condition first, and record whether the marker existed and the unit was active.
- Marker and unit pairs seen on the U8000F:

  - **Advertising:**
    - `pisa-service.service` → `pisa-service`
    - `canalysis-service.service` → `canalysis-daemon`
  - **Voice:**
    - `com.samsung.tv.bixby-prov.service` → `com.samsung.tv.bixby-prov`
    - `com.samsung.tv.ondevice-voice.service` → `com.samsung.tv.ondevice-voice`
    - `org.tizen.voice-app.service` → `org.tizen.voice-app`
    - `org.tizen.voice-client.service` → `org.tizen.voice-client`
  - **Context:**
    - `com.samsung.tv.context-aware-agent.service` → `com.samsung.tv.context-aware-agent`
    - `com.samsung.tv.context-aware-service.service` → `com.samsung.tv.context-aware-service`
    - `com.samsung.tv.AICompanionAgent.service` → `com.samsung.tv.AICompanionAgent`
  - **Smart extras:**
    - `com.samsung.tv.ghdaemon.service` → `com.samsung.tv.ghdaemon`
    - `samsunghealth-service-tv.service` → `samsunghealth-service-tv`
  - **TV Plus:** `slive-provider-daemon.service` → `slive-provider-daemon`

- Firmware-backed units without a marker condition can only be masked for the running boot
  (`systemctl mask --runtime`), and they come back at restart. Examples:
  - `ipa.service` (predictive app preload)
  - `contents-recognition-service.service`
  - `org.tizen.tv.sp-kpi.service` (performance reporting)
  - `data-service.service` (logging)
- The marker directory is not a general unit search path: custom units placed there were not shown to load. Owner
  user-manager units (`/opt/usr/home/owner/.config/systemd/user/`) do load. They run as the owner, not root.
- Some disabled units can still be started through dependencies. Check `ActiveState` and `ConditionResult` after
  a real reboot, not just live.

## Preferences (vconf)

- Read and write with `vconftool get` / `vconftool set -f -t TYPE KEY VALUE`. Save the old value first; the inverse
  is the same command with that value.
- Values used for a voice-free, preload-free setup:
  - `db/was/preload_by_country` (string) = `false`
  - `db/menu/autorun_tv_plus` (bool) = `0`
  - `db/menu/voice/voice_activated` (bool) = `0`
  - `db/menu/voice/wakeupless_status` (int) = `0`
  - `db/si_service/voice_recog_enable` (bool) = `0`
  - `db/multi-assistant/prelaunch_mode` (bool) = `0`
- The web-app service caches the preload flag in `.webappservice.db`, table `WASConfigTable`, row `RESERVED_18`,
  JSON path `$.rsp.preloadByCountry`. It is a JSON string (`"true"`), not a boolean.
  - Change only that field (`json_set`) after a backup, with a guard that it currently reads `"true"`.
  - Never copy an old database over the live one: the record also holds device details.
- Keys the launcher reads live (no root needed):
  - `memory/system/source_type`: current source; HDMI 1-4 = 13-16, TV = 0.
  - `memory/tv-viewer/mainscreen_signal_state`: 1 = picture on the current input. It flaps while an input syncs, so
    let it settle (~800 ms).
  - `memory/appfw/current_fullscreen_appid`: the app in front.
- Home and startup routing:
  - `db/appfw/home_appid` and `db/appfw/startup_appid`.
  - The app-framework main-operation Home handler, set with `launcher/probe` (`Probe.dll set-home APP_ID`).
  - All three were `com.samsung.tv.csfs` (Samsung Home) on the U8000F. Record yours before changing them.

## Native APIs usable from an app (owner user)

- **Settings:** `libsettings-api.so`, `CSharpGetItemValue` / `CSharpSetItemValue` and friends.
  - Ids are the short menuitem ids from `settings-datafw/jsons/tv/*.json` (`brightness`, `contrast`, `picturemode`,
    `game-mode-auto`, `sleeptimer`, `soundoutput`, `external-sound-mode`, `pixel-shift-menu`). Long catalog paths
    don't resolve.
  - Option lists can end with garbage, so keep only numeric values.
  - Each option has "enabled" and "visible" flags for the current input. Game Mode's item reports hidden while all
    of its values still apply.
- **Game Mode:** `game-mode-auto` is the real three-way setting (Auto / On / Off). `game-mode` only mirrors whether it
  is engaged right now.
- **Sources:** `libsource-api.so`.
  - `get_source_list()` returns malloc'd JSON. Bind it as `string` so the marshaller frees it.
  - `connect_source(type)` switches input. `register_sourcelist_changed_callback` pushes plug and rename events
    (renames fire nothing: re-read).
  - Names come from `editName`, then `detectedName` (HDMI-CEC).
  - `powerState` is unreliable behind a soundbar: devices report Off. Trust the signal key instead.
- **Volume:** `libcapi-media-sound-manager-tv.so` (`sound_manager_get/set_master_volume`, `..._mute`).
- **Launching:** `app_control` with extras.
  - `org.tizen.menu` opens a specific settings page with `menuitem=<id>`, `launchoption=pressed`,
    `__K_EX_FORCE_BOOST=enable` and `__K_SPLASH_SCREEN=LA_UI_VR_TRANSPARENT`.
  - `menuitem=add-bluetooth-keyboard` reaches the native Bluetooth device list, which pairs a DualSense despite the
    name. Pairing to the TV doesn't pass controller input through HDMI to a console.
- **Windows:**
  - An app can show a partial, transparent `WindowType.Notification` window at level Top over the TV viewer. It
    needs `http://tizen.org/privilege/window.priority.set`, which the bixby-promotion manifest already holds.
  - Add the aux hint `wm.policy.win.user.geometry=1` or the window manager makes it full screen.
  - The viewer keeps playing underneath and gets focus back when the window hides.
  - This is how SilentGlass draws over the picture. The VideoCanvas "live preview" route was unreliable.
- **Video plane (picture-in-card):** FLUX `VideoCanvasView` + `TVWindowControlAPI` (Channel List's mini-TV recipe)
  can show the live input inside an app, but it was fragile enough that SilentGlass dropped it for overlays:
  - the plane doesn't follow scale or position animations;
  - toggling `wm.tvs.win.use.tvs=1` per preview made the TV re-sync the input (signal flapping, rebuild loops); set
    it once and keep it until the app pauses;
  - crop rather than squash: `DisableAutoUpdateVideoGeometry` + `ForceUpdateVideoGeometry(x,y,w,h)` with a covering
    16:9 rect (the hardware crops the source to match);
  - the TV can leave the main video muted while the viewer is in the background: `IAVControl.MuteVideo(0)` on attach;
  - check placement without screenshots: `enlightenment_info -hwc_wins` (the `VD` plane's `dst_pos`); the video
    plane never appears in a UI capture.
- **Cold start and focus:** when an app launches, the TV shows a placeholder window (`[WIND]<appid>`) that holds the
  remote's keys until the app's main window has been shown. Hiding the main window in `OnCreate` leaves the
  placeholder in charge, so a notification window opened later appears but never receives a key. Let the main
  window show, then hide it.
- **Pause:** the TV pauses an app whenever its main window is hidden, even if one of its notification windows is up
  and focused. Use the foreground app (`memory/appfw/current_fullscreen_appid`), not `OnPause`, to decide that
  another app has taken over.
- **The signal flag** (`memory/tv-viewer/mainscreen_signal_state`) is only updated while the viewer runs: it still
  reads 1 in standby, and goes stale in Ambient. Treat it as "picture" only when the viewer is the app behind you, and
  debounce it (it flaps for ~1 s while an input syncs). **Power-off** makes the Home app the foreground app.
  While an app's own full-screen window is in front, the flag's change notification did not arrive (a SHIELD
  switched on under SilentGlass's wallpaper went unseen); poll it while you cover the viewer.
  **`memory/pm/state`** (1 on, 3 screen off, 4 standby) can be watched from an app to notice standby.
- **Window effects:** the compositor animates app windows in and out unless the window sets the aux hint
  `wm.comp.win.effect.enable=0`; set it on every window you show and hide yourself, the main window included.
- **HDMI-CEC:** SHIELD's sleep (Home Assistant `remote.turn_off`) also turns the TV off. A device waking with One
  Touch Play switches the TV's input underneath any open app window; a notification window that had the keys loses
  them.
- **The Home key reaches the Home app twice:** a key event plus a launch request, then a second launch request
  0.4-1 s later. Treat a Home launch within ~1.2 s of the last one as the same press, or the second undoes the first.
  After the Home app hides its windows on a physical-remote key (Back, Enter, Home), it gets one or two more launch
  requests 0.7-2.2 s later; Home Assistant's keys did not cause them.
- **Soundbar passthrough:** after a device behind the soundbar wakes the TV (CEC), the viewer can show black while
  the signal flag says picture; selecting the same input again (`connect_source` + launching the viewer) brings it
  up. Never treat "already on that input" as nothing to do. All Home launch requests, real and stray, come from
  `tvctx-manager`.
- **The signal flag is re-sent unchanged** whenever another app comes in front of the viewer; compare before acting.
- **Remote keys from Home Assistant** (Samsung TV integration, `remote.send_command`) can arrive seconds late or
  bunched together. Fine for occasional use; space test sequences out or check the app's own state between keys.
- **Rendering:** DALi renders every second vsync by default; `SetRenderRefreshRate(1)` gives 60 fps.
  `CornerSquareness` gives continuous corners. The TV's SVG renderer handles `fill-rule="evenodd"` but no masks or
  clip paths.
- **Power:** a uinput `KEY_POWER` from the owner session toggles power the way the remote does. Check the screen
  state first so the toggle is idempotent.
- **Thread:** the SmartThings hub's `ot-ctl` (`/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/ot-ctl`) works as
  the owner user, so an app-side service can read and set datasets.
  - Only replace the active dataset with Thread stopped (`thread stop; ifconfig down`). Committing on a running
    node pushes the change to the whole mesh.

## Security boundaries seen

- **SMACK:** other apps' `/proc` and vendor marker files are unreadable to an app. Show "unknown" rather than
  inferring from a failed read.
- **UEP:** unsigned script files are refused ("the file is NOT signed!!"). Feeding the same text to `/bin/sh` on
  stdin works.
- **Native images:** a stale `.native_image` cache next to an app's DLL keeps running the old assembly. Move it
  aside when replacing the DLL.
- **Root:** root obtained through the SDK route is still constrained by SMACK and UEP. Nothing here unlocks secure
  boot or installs a permanent root service.

## Privacy: what disabling does and doesn't mean

- ACR is best described as **screen-content fingerprinting** under Samsung's optional Viewing Information Services
  ([Samsung](https://www.samsung.com/us/support/answer/ANS10010616/)).
  - A [2024 measurement study](https://arxiv.org/html/2409.06203v1) saw ACR traffic on tested Samsung and LG TVs used
    as HDMI displays.
  - Opting out stopped it in that study. It concerned other models.
- "Disabled" here means registry flags, stop conditions and process checks, not a proven end to every data flow. A
  firmware or catalog update can bring paths back.
- Use Samsung's own privacy and ACR opt-outs as well; they need no modification.
- A wake-word engine or a package called `content-grab-service` is not by itself evidence of room audio or screen
  captures leaving the TV. Nothing in this work showed camera spying, constant recording or screenshot uploads.
- Stronger claims need a consented before/after network measurement.

## Performance notes

- Separate the symptoms:
  - **Slow Home or sidebar:** launcher startup, preload requests, stale native images.
  - **Slow settings:** use native API controls (as SilentGlass does) and keep Samsung's UI for rare screens.
  - **Lag inside one streaming app:** memory pressure and that app, not Bluetooth.
  - **Buffering:** network.
- Background preloads (TV Plus, Prime, YouTube) eat memory. After closing them and stopping preloads, U8000F
  `MemAvailable` went from about 0.5 GiB to about 0.9 GiB. CMA is a shared graphics/media pool, not VRAM.
- `app_launcher -u 5001 -r APP_ID` returns 1 when the app is already gone; handle it. A launch request or a new PID
  doesn't prove the screen is visible.
- `enlightenment_info -fps` hung. `enlightenment_info -topvwins` (window stack and focus) is safe.

## Recovery principles

- Keep Developer Mode and computer access working until the modified TV has passed full boot, standby and wake,
  HDMI, sound and remote tests.
- Don't factory-reset first: it can remove recovery state without restoring modified app files.
- Change one reversible phase at a time, with a new backup name each phase. Never delete or overwrite the first
  original backup to make an installer rerun.
- Restore field by field (only what the phase changed). If a drift guard trips, reconcile instead of removing the
  guard.
- If access stops working after an update, re-establish model, firmware and capability evidence. Never use another
  model's kernel route or an unverified image.
