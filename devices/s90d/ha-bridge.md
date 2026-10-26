# Home Assistant bridge

`silentglass-ha` puts the TV into Home Assistant as one MQTT-discovered device, with live state and a few controls.
It is a 23 KB .NET program (`device/ha-bridge/src`, run by the TV's own `dotnet`, about 20 MB RSS) with a minimal
built-in MQTT 3.1.1 client. It runs as the TV's owner user from a systemd user unit, not as root.

## What Home Assistant gets

| Entity | What |
|---|---|
| `binary_sensor.<tv>_screen` | on / off (standby). The TV stays on the network in standby, so this is not "unavailable" |
| `select.<tv>_input` | the input on screen; choosing one switches to it through SilentGlass Home, like a card press |
| `sensor.<tv>_app` | foreground app: SilentGlass Home, Ambient, Samsung Home, or the input's name while watching it |
| `sensor.<tv>_thread_role` | router / child / leader / detached / off (only if the border router is installed) |
| `switch.<tv>_thread_border_router` | same as the Info switch; survives reboots |
| `button.<tv>_home`, `button.<tv>_ambient` | open SilentGlass Home / Ambient |
| `sensor.<tv>_chip_temperature`, `sensor.<tv>_free_memory` | diagnostics |

The device shows as unavailable only when the TV is unplugged or rebooting (retained MQTT last will).

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
