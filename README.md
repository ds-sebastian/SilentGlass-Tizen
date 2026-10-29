# SilentGlass-Tizen

**A calmer, more private Samsung TV.** An Apple-style Home over your streaming box and console, Home Assistant
built in, and a Thread border router on your own network.

Tested on one TV: Samsung **QN65S90D** (2024 OLED), firmware `T-PTMDAKUC-0090-REL-202605141701`.

> **Unofficial, at your own risk.** Needs root access from a separate tool. Bricking is unlikely but possible.
> Read [Risks](#risks) first.

![The bar over the picture](docs/screenshots/s90d-overlay-bar.jpg)

## Features

- **Home as an overlay.** A glass bar fades in over whatever is playing. Watch and Play jump to your streaming box
  and console. The picture never stops.
- **Controls without menus.** Brightness, volume, picture and sound modes, Game Mode and the sleep timer in a side
  panel.
- **Home Assistant.** Power, inputs, volume and picture settings, plus camera and doorbell notifications over any
  input. Never wakes the TV.
- **Thread border router.** The TV's built-in radio joins your existing Thread network and keeps running in
  standby.
- **Privacy.** Content recognition (ACR), Bixby and other voice assistants, ad targeting and recommendations are
  off. HDMI, eARC, Ambient/Art, AirPlay and phone power-on still work.
- **Set up from your phone.** Scan a QR code on the TV. No app needed.

![Controls over the picture](docs/screenshots/s90d-overlay-controls.jpg)

<p><img src="docs/screenshots/s90d-overlay-inputs.jpg" width="49%" alt="Inputs, with a green dot on every device that is on">
<img src="docs/screenshots/s90d-notice.jpg" width="49%" alt="A doorbell notification over the picture"></p>

![Advanced settings on a phone](docs/screenshots/s90d-phone-settings.jpg)

<sub>The TV can't capture its own video, so each screenshot is the TV's real UI over a still from *Big Buck Bunny*
(© Blender Foundation, CC BY 3.0). Addresses and codes are stand-ins.</sub>

## Risks

| Risk | How likely | The fix |
|---|---|---|
| TV won't start | Very unlikely: firmware, bootloader and system files are never touched | — |
| Apps won't launch | Unlikely: the app registry is backed up and changed in one checked step | restore script |
| Home does nothing | Possible after a bad update; the TV still turns on to the last input | `undo.sh`, or back to Samsung Home |
| Thread or SmartThings stops | Possible after the radio flash; nothing else is affected | flash again with Samsung's tool |
| No security updates | Certain: automatic updates are off so they can't remove root access | install updates yourself |
| Warranty | Samsung may refuse service on a modified TV | — |

- Every undo needs root, from [s90c-tools](https://github.com/invaliddayta/s90c-tools)' `tv-access` (Developer
  Mode and a supported firmware).
- Most undo scripts were tested against copies of the TV's files, not on a real TV. Batch G and Thread were undone
  on the TV.
- Copy the backups off the TV. A factory reset isn't a tested undo.

## What it changes

- **One Samsung app is taken over:** *Bixby promotion*, a Bixby ad screen. It already has permission to draw over
  the picture. The original is backed up.
- **Background services**, all optional and run as the TV's normal user:
  - `silentglass-ha`: the Home Assistant bridge and phone page;
  - `quiet-recog-off`: stops content recognition when Samsung restarts it;
  - `quiet-csfs-off`: closes Samsung Home when it idles hidden in the background (~100 MB);
  - `quiet-otbr-*`: the Thread border router.
- **One-time settings:** apps disabled, automatic updates off, Multi View not preloaded, and the Thread radio
  switched to Samsung's hub firmware.

Full list: [What it installs](devices/s90d/README.md#device-changes).

## Install

Follow [docs/install.md](docs/install.md): install the launcher, make it Home, then add the bridge, Thread and
privacy steps you want. Updates are one bundle and one `install.sh`.

Another model or firmware needs its own check first. Same brand or year doesn't mean compatible. With a coding
agent, start from [AGENTS.md](AGENTS.md).

## Repository

- [launcher/src](launcher/src): SilentGlass (.NET 6, Tizen NUI)
- [devices/s90d](devices/s90d/README.md): the tested TV's scripts, the [Home Assistant](devices/s90d/ha-bridge.md)
  and [Thread](devices/s90d/thread-border-router.md) guides, the [changelog](devices/s90d/changelog.md)
- [docs](docs): install guide, [Samsung service notes](docs/samsung-tizen-notes.md), agent workflow
- [tests](tests), [tools](tools): offline tests, a simulated TV, build and release tools

```sh
python3 -m unittest discover -s tests   # offline tests
python3 tools/release.py VERSION         # release files; builds are reproducible
```

## Credits

Started as a fork of [Quiet Tizen](https://github.com/jss8649/quiet-tizen). Fonts: Inter (OFL). Details in
[NOTICE.md](NOTICE.md). Not affiliated with Samsung. MIT license.
