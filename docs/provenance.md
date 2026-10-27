# Provenance and verification scope

## Device work

Everything was developed and checked on one owner-authorized QN65S90DAFXZA (firmware
`T-PTMDAKUC-0090-REL-202605141701`) between 2026-10-21 and 2026-10-26, building on the Quiet Tizen work done on a
U8000F (2026-10-19/20). Private originals - registry snapshots, logs, network addresses, credentials - stay outside
this repository. [Tizen notes](samsung-tizen-notes.md) record what was observed and on which TV.

Administrative access came from the external [s90c-tools](https://github.com/invaliddayta/s90c-tools) `tv-access`
helper (not bundled); its scope and limits are summarized in [upstream notes](upstream-notes.md).

## Launcher and bridge

SilentGlass (the launcher), Probe and the Home Assistant bridge were written for the
tested TV and compiled against assemblies from that TV. The UI was designed with Pi-rendered HTML mockups
(`tools/overlay_mockup.py`) before each on-TV check.

## What local validation establishes

- The launcher, bridge and Probe compile against the matching locally held references.
- C# tests cover the NUI-free logic: preferences, Watch/Play cycling, banner geometry, sun times, QR encoding.
  The QR encoder's output is also decoded with OpenCV on the Pi.
- The Home Assistant bridge's notification parser is driven by Home Assistant's own client library, and its phone
  setup page runs end to end against a fake MQTT broker (code gate, validation, saved config, reconnect).
- Shell recipes and device scripts receive syntax and integrity checks; selected SQL is tested against synthetic
  tables.

These checks do not substitute for on-TV visual and remote checks, reboot and standby acceptance, or compatibility
testing on another model or firmware.
