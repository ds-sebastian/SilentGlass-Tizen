# Samsung QN65S90DAFXZA (S90D, 2024 OLED)

This fork's working target. Owner setup: **SHIELD TV Pro** (via the Q990D soundbar, HDMI 3) is the main
device, **Nintendo Switch** on HDMI 1; the TV's own apps are backups. Every change below was applied with a
backup, a guarded inverse and hash verification, and re-checked after reboots.

- Firmware: `T-PTMDAKUC-0090-REL-202605141701` (system build string `TIZEN-TRUNK2025-PontusM-OSU-2024-HOTFIX-RELEASE_20260514.3`)
- Admin access: per-command uid-0 sessions over SDB (see `docs/02-access.md`); scripts are fed to `/bin/sh` on
  **stdin** - unsigned script files cannot be executed under UEP.

## Quiet Home on the S90D (v15)

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
- **Live preview: built, switched off** (`LivePreviewEnabled`). FLUX `VideoCanvasView` + `TVWindowControlAPI`
  (Channel List's mini-TV recipe, hint `wm.tvs.win.use.tvs=1`) attached, but the video plane lagged the card's focus
  scale/lift (black cut-out while focused) and the signal flag flapped (40 events). Needs on-screen tuning:
  no focus transform on the video card, fixed geometry, GV-sync mode.
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
