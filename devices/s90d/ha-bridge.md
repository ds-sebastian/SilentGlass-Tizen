# Home Assistant bridge

`silentglass-ha` puts the TV into Home Assistant as one MQTT-discovered device, with live state and a few controls.
It is a 23 KB .NET program (`device/ha-bridge/src`, run by the TV's own `dotnet`, about 20 MB RSS) with a minimal
built-in MQTT 3.1.1 client. It runs as the TV's owner user from a systemd user unit, not as root.

## What Home Assistant gets

| Entity | What |
|---|---|
| `switch.<tv>_power` | on / standby, switched on the TV itself (see Power) |
| `select.<tv>_input` | the input on screen, by the TV's own input names; choosing one switches to it like a card press |
| `text.<tv>_hdmi_N_name` | the port's name on the TV (attributes: the HDMI-CEC detected name, connected). Editing it renames the input everywhere: TV menu, SilentGlass Home, this selector. Ports only take a name once a device has been seen on them |
| `button.<tv>_resync_input_names` | re-scans HDMI-CEC and fixes stale detected names (see below) |
| `number.<tv>_volume`, `switch.<tv>_mute` | TV master volume (also drives the soundbar over ARC) |
| `number.<tv>_brightness`, `number.<tv>_contrast` | picture settings (the TV's own ranges, 0-50) |
| `select.<tv>_picture_mode`, `select.<tv>_sound_mode` | only the modes the TV offers right now (they change with the input and the soundbar) |
| `select.<tv>_sound_output` | TV Speaker / Soundbar |
| `select.<tv>_sleep_timer` | |
| `select.<tv>_game_mode` | Automatic / Always / Never - the TV's one Game Mode setting (Auto / On / Off). Bridge 2.0-2.1 had a switch that drove the value which only mirrors whether Game Mode is engaged |
| `switch.<tv>_notifications` | banners on the TV (see Notifications); off drops them |
| `sensor.<tv>_app` | foreground app: SilentGlass Home, Ambient, Samsung Home, or the input's name while watching it (also while SilentGlass only shows a banner over it) |
| `sensor.<tv>_thread_role` | router / child / leader / detached / off (only if the border router is installed) |
| `switch.<tv>_thread_border_router` | same as the Info switch; survives reboots |
| `button.<tv>_home`, `button.<tv>_ambient` | open SilentGlass Home / Ambient |
| `sensor.<tv>_chip_temperature`, `sensor.<tv>_free_memory` | diagnostics |

The device shows as unavailable only when the TV is unplugged or rebooting (retained MQTT last will).

## Notifications

Home Assistant can show notifications **on the TV itself, over whatever input is on screen** (SHIELD, the Switch,
an app), as a tvOS-style banner from SilentGlass Home - with the picture (a Frigate snapshot, a doorbell frame), title
and message. The bridge speaks the protocol of the *Notifications for Android TV* app, so the TV is added with Home
Assistant's built-in integration and every existing automation works unchanged:

1. Settings > Devices & services > Add integration > **Notifications for Android TV / Fire TV**, host = the TV's
   address. This makes a `notify.android_tv_fire_tv_<address>` action (and a notify entity).
2. Use it directly, or put it in a notify group (`platform: group`) that your automations already target - e.g. the
   SgtBatten Frigate blueprint's *notify group* with its TV options (`tv_position`, `tv_size`, `tv_duration`).

What is honoured: `title`, `message`, `data.image` (URL or path; Home Assistant downloads it and sends the bytes - JPEG,
PNG, GIF, WebP), `data.duration` (seconds, 3-60 on screen), `data.position` (bottom-right, bottom-left, top-right,
top-left, center) and `data.fontsize` (small / medium / large / max = the banner width). `transparency`, `color` and
`interrupt` are accepted and ignored: the banner never takes the remote, and its glass is always readable.

Rules:
- **Never wakes the TV.** In standby a notification is dropped (Home Assistant still gets *OK*); so is one while
  Ambient is up, or while `switch.<tv>_notifications` is off.
- A newer notification replaces the one on screen.
- Accepted only from the MQTT broker's host (normally Home Assistant's machine) and the addresses in `notify_from=`
  of `silentglass-ha.conf`; anything else is refused. Port 7676, one request per connection, at most 12 MB.
- The banner is SilentGlass's own small notification-level window that never takes focus; the video is not touched.

How it travels: the bridge writes the image and `notice.json` into SilentGlass Home's data folder (the app's own
SMACK label, so the app may read them); the running app picks it up at once with a file watcher and deletes it as the
acknowledgement. SilentGlass is closed whenever the TV goes to standby, so the first notification after power-on
finds it not running: the bridge then starts it with `quiet_notice=1`, and it starts **hidden** (no flash, the viewer
stays in front, ~1 s later the banner appears). It is only started like that over the TV viewer (inputs); over another
app it would push that app to the background, so the notification is dropped there instead and logged.

Checked on the TV: dropped in standby (no wake); shown on a cold start (SilentGlass started hidden) and from the
watcher; remote focus stayed on the viewer (`enlightenment_info -topvwins` Foc column), the HDMI signal never dropped,
and Home still opened the bar afterwards. Off the TV, `tests/test_s90d_notice.py` runs Home Assistant's own client
library (notifications-android-tv 0.1.5) against the bridge's request reader and parser (`tests/notice-harness`).

## Input names

Each port has two names on the TV: the one you set (`editName`, TV menu or Home Assistant) and the one the device
announced over HDMI-CEC (`detectedName`). Everything shows the set name, else the detected one, else "HDMI n";
nothing is hard-coded. Detected names go stale: the TV keeps the last device it saw on a port (HDMI 1 said
"PlayStation 5" long after the Switch replaced it), and a device behind a soundbar shows up as the soundbar.

**Resync input names** asks the TV for a fresh HDMI-CEC device search (20 s), then sets each port's detected name to
what answers on it now. With the TV on, ports where nothing answers are cleared; in standby (devices asleep) names
are only learned, never cleared. The TV resets a port's own name when its detected name changes, so the bridge puts
it back. Details: the names live in "activity index" -2 (Samsung's default; index 0 is a separate, unused slot), and
the source library only refreshes its cached list from D-Bus signals, so the bridge runs a GLib loop for them.

## Power

The bridge presses Power through its own virtual key: a `/dev/uinput` device ("SilentGlass power key") that can only
send KEY_POWER. That is how Samsung's own remote server (`msf-server`, via `libautoinput`) sends remote keys, and
`/dev/uinput` is open to the owner user on this TV, so no root and nothing in Home Assistant is needed. Power is a
toggle, so the bridge only presses it when the TV is not already in the requested state. It wakes the TV from
standby and puts it into standby.

Ruled out on the way: deviced's `device_power_set_state(Normal)` and D-Bus `ChangePowerState`/`changestate` return
-22 even as root (wake is event-driven, not a settable state), `device_power_wakeup` is not supported, `input_keyevent`
is root-only, and the TV's remote WebSocket ignores clients on 127.0.0.1.

## How it works

- State is read every 2 s from cheap local sources: `memory/pm/state` (4 = standby),
  `memory/eden/source/current_source` (`SOURCE_TYPE_HDMIn`), `memory/appfw/current_fullscreen_appid`, SilentGlass
  Home's input list in `/tmp/quiet-home-state.txt`; temperature, memory and the Thread role (`ot-ctl state`) every
  30 s. Only changes are published (retained), plus a full refresh every 5 minutes and whenever Home Assistant
  restarts (`homeassistant/status`).
- Commands are a fixed list; there is no shell. Input: `launch_app com.samsung.tv.bixby-promotion quiet_source <id>`
  (the launcher only accepts an input it lists). Home / Ambient: `launch_app`. Thread: writes or removes the
  `thread-off` marker and touches `thread-switch`, exactly like the Info switch.
- Topics: `silentglass/s90d/...`; discovery under `homeassistant/<component>/silentglass_s90d/...`.

## Set up from a phone

On the TV: **Home > Info > Home Assistant**. A sheet shows a QR code and a 6-digit code; scan the code with a phone on
the same network (or open the address shown and type the code). The page, served by the bridge on port 7680, has:

- **Home Assistant**: MQTT server, port, username, password, with a **Test connection** button. The password is never
  sent back to the phone; leave it empty to keep the current one.
- **This TV**: the name it has in Home Assistant.
- **Notifications**: on/off, and extra addresses allowed to send them (`notify_from`) if Home Assistant doesn't run
  on the broker's host.
- **Thread** (where the border router is installed): keep the current network, read Home Assistant's preferred
  network once (address + a long-lived token; the token is used for that one request and not stored), or paste a
  dataset. The dataset is applied as the owner user with `ot-ctl`, with Thread stopped first so nothing is pushed
  to the rest of the mesh.
- **Wallpaper**: latitude/longitude for the sunrise/sunset switch.

Save writes the owner-only config, reconnects, and the sheet on the TV shows the result. The page only works while
the sheet is open: Back on the TV, 10 minutes, or 8 wrong codes close it. It is plain HTTP on your LAN, like the TV's
own setup flows; the code keeps others on the network out while it is open.

## Install

1. On the TV, as root: feed `device/ha-bridge/ha-bridge-install.sh` to `/bin/sh` on stdin. The bridge starts
   unconfigured (Info shows *Set up*).
2. Set it up from a phone (above). By hand instead: copy `/opt/usr/home/owner/.config/silentglass-ha.conf.example`
   to `silentglass-ha.conf` (owner, mode 600, same SMACK label as the home directory), fill in the broker and
   restart the unit.
3. The device appears in Home Assistant under Settings > Devices (MQTT).

The credentials live only in that file on the TV, never in the repo. A broker user limited to `silentglass/#` and
`homeassistant/+/silentglass_s90d/#` is the tidy choice; a shared house user works too.

Remove: `ha-bridge-restore.sh` (then delete the device in Home Assistant to clear the retained discovery topics).
Log (events only): `/run/user/5001/silentglass-ha.log`; status for Info: the launcher's `data/bridge-status.txt`.
