# Samsung QN65S90DAFXZA (S90D, 2024 OLED)

The tested TV, set up like this:
- a **SHIELD TV Pro** on HDMI 3, through a Q990D soundbar: the main device;
- a **Nintendo Switch** on HDMI 1;
- the TV's own apps, kept only as backups.

- **Firmware:** `T-PTMDAKUC-0090-REL-202605141701`.
- **Root:** [s90c-tools](https://github.com/invaliddayta/s90c-tools)' `tv-access`, not bundled. Scripts go to
  `/bin/sh` on stdin, because the TV refuses unsigned script files.
- **Agents:** start with the [agent workflow](../../docs/agent-workflow.md). The records here are dated evidence.
  Read the TV's state before acting.

## SilentGlass

The Home screen, installed in place of `com.samsung.tv.bixby-promotion`. Power-on still goes to the last input.
History: [changelog.md](changelog.md).

![The bar over the picture](../../docs/screenshots/s90d-overlay-bar.jpg)

Everything is an overlay over the TV's own viewer, so the picture keeps playing and the video is never touched.

| Part | What it does |
|---|---|
| **Bar** | Watch and Play (your two main inputs), Inputs, Controls, Apps, Info. Home toggles it, and it hides after 20 s idle. |
| **Inputs** | every input by the TV's name. A green dot means its device is on (picture or HDMI-CEC). |
| **Controls** | brightness, contrast, volume; picture, sound and Game Mode; sleep timer, pixel shift; Ambient, Bluetooth, settings |
| **Apps** | the TV's apps as artwork (backups; the SHIELD has the real ones) |
| **Info** | privacy, network, Thread and Home Assistant status, settings, and Advanced settings (phone) |
| **Wallpaper** | shown only when there's no picture, with a clock; dims after 5 min for the OLED |
| **Banners** | Home Assistant notifications and input alerts, in their own window, which never takes the remote |

<p><img src="../../docs/screenshots/s90d-overlay-inputs.jpg" width="49%" alt="Inputs">
<img src="../../docs/screenshots/s90d-info.jpg" width="49%" alt="Info"></p>

### Settings

These are on Info or the phone page, and stored in `silentglass-prefs.txt`.

| Setting | Default | Options |
|---|---|---|
| Watch / Play | Automatic | the inputs named `Shield` / `Switch` (else HDMI 3 / 1), or any HDMI port |
| Apps in the bar | on | off hides Apps |
| Menu closes after | 20 s | 10 s, 20 s, 30 s, 1 min, never |
| Dim after | 5 min | 2, 5, 10, 30 min, never |
| Clock | 12-hour | 24-hour, hidden |
| Wallpaper | Auto | sunrise/sunset, Day, Night (photo wallpaper only) |
| Notifications / Input alerts | on | [ha-bridge.md](ha-bridge.md) |
| Thread border router | on | [thread-border-router.md](thread-border-router.md) |

The photo wallpaper is Apple artwork and isn't included. To build it from your own copies, run
`tools/make_wallpapers.py DAY.jpg NIGHT.jpg`.

### How it works (for developers)

- **Inputs** are pushed by the TV, with no polling:
  - `libsource-api` `register_sourcelist_changed_callback`;
  - vconf `memory/system/source_type`, `memory/tv-viewer/mainscreen_signal_state` (settled 800 ms) and
    `memory/appfw/current_fullscreen_appid`.
  - `powerState` is ignored, because the SHIELD reports Off from behind the soundbar.
- **Screen.cs** alone decides between wallpaper, bar and nothing. `tests/screen-sim` checks it against a simulated
  TV:
  - the wallpaper never covers a picture;
  - a visible bar always gets the keys;
  - Home always leaves a working bar.
- **Ambient:** the Art app always hands Home to Samsung Home, so SilentGlass takes it back.
- **Cold start:**
  - The launch placeholder holds the keys until the main window has been shown once, so it starts shown (black).
  - The TV pauses the app while only the overlay is up, and a pause never closes it.
- **Launch extras:**
  - `sg_source=<type>` switches input;
  - `sg_notice=1` starts hidden for a banner;
  - `sg_open=controls|info|apps|inputs|setup` opens one part directly.
- **State file:** `/tmp/silentglass-state.txt`, which records what's showing, plus inputs and settings.
- **Shared with the bridge:** [`Shared.cs`](../../launcher/src/Shared.cs) has app ids, file names and the version.
- **Speed rules, enforced by tests:**
  - no IPC or file I/O on the key path;
  - native callbacks are posted to the UI thread.
- **Deploy:**
  - `device/install.sh` installs the bundle. It checks every file's sha256, keeps SMACK labels and backs up what it
    replaces.
  - `device/undo.sh` puts one install back.
  - `launcher-deploy/restore-stock-home.sh` goes back to Samsung Home from any version.

## Device changes

Each has its own undo script in `device/`.

| Change | What | Undo |
|---|---|---|
| privacy/ (B) | disables content recognition (ACR), the Bixby/Alexa voice stacks and TV Plus | restore.sh |
| cleanup/batchc | ad targeting (`targeted-ad-service`), voice leftovers | batchc-restore.sh |
| cleanup/batche | ACR content feed (`cis`), usage analytics, always-on voice, ad players | batche-restore.sh |
| cleanup/batchd + batchf/f2 | Prime Video, Apple TV and the browser disabled (the SHIELD has them); TV YouTube off (cast to the SHIELD) | batchd/batchf/batchf2-restore.sh |
| cleanup/prime* | Prime Video boot metadata and its companion | prime-restore.sh, prime-noboot-restore.sh |
| cleanup/multiview | Multi View no longer preloads at boot | multiview-restore.sh |
| cleanup/batchg | Samsung's hidden app upgrader and firmware auto-update off | batchg-restore.sh |
| cleanup/reapply | re-disables anything an update turned back on | reapply-restore.sh |
| boot-units/quiet-recog-off | stops content recognition when boot restarts it | recog-boot-restore.sh |
| boot-units/quiet-csfs-off | closes Samsung Home after it idles hidden for 2 min (~100 MB) | csfs-off-restore.sh |
| ha-bridge | [Home Assistant bridge](ha-bridge.md) | ha-bridge-restore.sh |
| thread-border-router | [Thread border router](thread-border-router.md) | otbr-restore.sh |

**Lessons from these changes:**
- **Uninstalled preloads come back.** Prime Video, Apple TV and the browser returned ~3.5 h after being
  uninstalled, so batch F disables them instead.
- **Batch E keeps three services on purpose:**
  - `coba.adagent`, because Samsung Home loads it;
  - `content-grab-service` and `nvision-analysis-dbmgr`, which may feed picture processing and Ambient.
- **Closing these apps is pointless:** TV YouTube (`cobalt-yt`), Samsung Home and its media rows relaunch within
  seconds to a minute. That only burns CPU.
- **Memory:** after batch E, Multi View and the csfs watcher, free memory is ~780-835 MB with the TV on, up from
  ~600-630 MB.
- **Worth checking on other TVs:** firmware auto-update (`db/menu/support/softwareupdate/autoupdate`) and Smart Hub
  autostart (`db/menu/smart_hub/ontvsetting/auto_start`). Both were already off on this one.
- **Block in DNS:** two diagnostic-log hosts, `fc.samsungcloud.tv` and `www.samsungrm.net`. They're used by a root
  service that can't be stopped.

**Running services:**
- **Install:** `*-install.sh`, then the latest `*-update*.sh`.
- **Where they live:** `/opt/usr/home/owner/.config/systemd/user`, label `User::Home`.
- **Control:** use `DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001
  systemctl --user`.
- **Syntax check:** `sh -n < file`. The TV blocks `sh -n file`.

## Pi side (pi/)

- **`s90d_drift_check.py`:** a weekly read-only check of firmware, Home, disabled apps, update settings, DLL hash
  and services. It's silent when all is well.
- **`thread_dataset.py`:** saves Home Assistant's Thread network to a private file.
- **`probe_forwarding.py`:** proves with one packet that the TV routes to Thread.

## Findings

- The settings API takes the short menu ids from `settings-datafw/jsons/tv/*.json`.
- `get_source_list()` returns a malloc'd string, so bind it as `string`.
- `8.8.8.8` is a connmand fallback. A router "force local DNS" rule handles it.
- **Soundbar input flips** (eARC ↔ HDMI 1) when switching to or from the SHIELD are CEC active-source changes.
  Wiring the SHIELD straight to the TV avoids them.
