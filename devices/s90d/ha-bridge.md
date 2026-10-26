# Home Assistant bridge

`silentglass-ha` puts the TV into Home Assistant as one MQTT-discovered device, with live state and a few controls.
It is a 23 KB .NET program (`device/ha-bridge/src`, run by the TV's own `dotnet`, about 20 MB RSS) with a minimal
built-in MQTT 3.1.1 client. It runs as the TV's owner user from a systemd user unit, not as root.

## What Home Assistant gets

| Entity | What |
|---|---|
| `switch.<tv>_power` | on / standby. Turning it on or off needs the small Home Assistant automation below |
| `select.<tv>_input` | the input on screen, by the TV's own input names; choosing one switches to it like a card press |
| `text.<tv>_hdmi_N_name` | the port's name on the TV (attributes: the HDMI-CEC detected name, connected). Editing it renames the input everywhere: TV menu, SilentGlass Home, this selector. Ports only take a name once a device has been seen on them |
| `button.<tv>_resync_input_names` | re-scans HDMI-CEC and fixes stale detected names (see below) |
| `number.<tv>_volume`, `switch.<tv>_mute` | TV master volume (also drives the soundbar over ARC) |
| `number.<tv>_brightness`, `number.<tv>_contrast` | picture settings (the TV's own ranges, 0-50) |
| `select.<tv>_picture_mode`, `select.<tv>_sound_mode` | only the modes the TV offers right now (they change with the input and the soundbar) |
| `select.<tv>_sound_output` | TV Speaker / Soundbar |
| `select.<tv>_sleep_timer`, `switch.<tv>_game_mode` | |
| `sensor.<tv>_app` | foreground app: SilentGlass Home, Ambient, Samsung Home, or the input's name while watching it |
| `sensor.<tv>_thread_role` | router / child / leader / detached / off (only if the border router is installed) |
| `switch.<tv>_thread_border_router` | same as the Info switch; survives reboots |
| `button.<tv>_home`, `button.<tv>_ambient` | open SilentGlass Home / Ambient |
| `sensor.<tv>_chip_temperature`, `sensor.<tv>_free_memory` | diagnostics |

The device shows as unavailable only when the TV is unplugged or rebooting (retained MQTT last will).

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

The bridge runs as an ordinary user: it can read the power state but cannot press Power (key injection is root-only,
and the TV's remote-control API refuses local clients). Home Assistant's Samsung integration can, so the Power switch
publishes to `silentglass/s90d/power/set` and a small automation presses KEY_POWER when the TV is not already in the
requested state: [`device/ha-bridge/ha-power-automation.yaml`](device/ha-bridge/ha-power-automation.yaml).

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

## Install

1. On the TV, as root: feed `device/ha-bridge/ha-bridge-install.sh` to `/bin/sh` on stdin.
2. Copy `/opt/usr/home/owner/.config/silentglass-ha.conf.example` to `silentglass-ha.conf` (owner, mode 600, same
   SMACK label as the home directory) and fill in the broker. Optional `input_<id>=Label` lines name the inputs
   (HDMI1=13 ... HDMI4=16). The unit starts once the file exists.
3. The device appears in Home Assistant under Settings > Devices (MQTT).

The credentials live only in that file on the TV, never in the repo. A broker user limited to `silentglass/#` and
`homeassistant/+/silentglass_s90d/#` is the tidy choice; a shared house user works too.

Remove: `ha-bridge-restore.sh` (then delete the device in Home Assistant to clear the retained discovery topics).
Log (events only): `/run/user/5001/silentglass-ha.log`.
