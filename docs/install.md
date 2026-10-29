# Install SilentGlass

Read [Risks](../README.md#risks) first. Do the steps in order, check each on screen, and copy the backups off the
TV.

**You need:**
- root on the TV, from [s90c-tools](https://github.com/invaliddayta/s90c-tools)' `tv-access` (not included; see
  the [agent walkthrough](agent-workflow.md#2-confirm-the-existing-access-path));
- a way to copy files to `/home/owner/share/tmp/sdk_tools/quiet-tizen/` (e.g. `sdb push`).

Run every script by feeding it to a root shell: `/bin/sh -s -- ARGS < script`. The TV refuses unsigned script
files.

## 1. Get the files

**From a release:** download the bundle, `install.sh`, `undo.sh` and `SHA256SUMS`. Check them with
`sha256sum -c SHA256SUMS`, then copy the bundle to the TV as `quiet-tizen/package.tar.gz`.

**Or build it:** copy `Tizen.NUI.dll`, `Tizen.Applications.Common.dll`, `Tizen.System.dll` and `Tizen.dll` from
the TV's `/usr/share/dotnet.tizen` into `.local/tv-framework/` (Samsung's files, never committed), then:

```sh
python3 tools/build.py launcher --framework .local/tv-framework
python3 tools/build.py bridge
python3 tools/bundle.py --public   # .local/bundle/silentglass-<version>.tar.gz
```

Builds are reproducible: the same commit gives the same bytes as the release.

- **Wallpaper:** the bundle uses the built-in background. To use photos, add `wallpapers/wall-day.jpg` and
  `wall-night.jpg`.
- **Apps menu:** to list your own apps, add `launcher/assets/apps.local.json` ([app library](app-library.md)).

## 2. Install the launcher

```sh
/bin/sh -s -- home-01 < recipes/snapshot.sh          # backup: registry, settings, Home routing
/bin/sh -s -- home-01 < recipes/install-launcher.sh  # with the bundle copied to the TV
```

This puts SilentGlass inside Samsung's *Bixby promotion* app, with the original backed up to
`/opt/usr/share/quiet-tizen-backups/home-01/`. Home doesn't change yet.

**Check:** the app opens with the bar, Watch/Play/Inputs switch inputs, and Controls and Info work.

## 3. Make it Home

Write down the three current values first (the snapshot saves them too): the Home handler, `db/appfw/home_appid`
and `db/appfw/startup_appid`. Then:

```sh
/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/home-01/Probe.dll set-home com.samsung.tv.bixby-promotion
vconftool set -f -t string db/appfw/home_appid com.samsung.tv.bixby-promotion
vconftool set -f -t string db/appfw/startup_appid org.tizen.tv-viewer   # power on to the last input
```

**Check:** read all three back, then try a reboot, Home, standby and wake, and coming back from an app.

## 4. Optional extras

- **Home Assistant bridge:** `/bin/sh -s -- bridge < devices/s90d/device/install.sh`, then on the TV open
  **Info › Advanced settings** and scan the QR code. [Guide](../devices/s90d/ha-bridge.md).
- **Stop silent updates** (do this before the privacy steps): `devices/s90d/device/cleanup/batchg-apply.sh`. The
  menu's "Auto update" switch isn't enough. Samsung's upgrader re-enables disabled apps on its own.
- **Thread border router:** [guide](../devices/s90d/thread-border-router.md).
- **Privacy steps:** listed with their undo scripts in the [S90D record](../devices/s90d/README.md#device-changes).

## Updating

Copy the new bundle to the TV as `quiet-tizen/package.tar.gz` and run `/bin/sh < install.sh`.

- It updates from any SilentGlass version, plus the bridge if it's installed. `all` adds the bridge.
- It checks every file first and stops without changes if one is wrong.
- It keeps your settings and prints a backup name. To go back: `/bin/sh -s -- <name> < undo.sh`.

## Undo

Undo the newest change first, and the launcher last:

- **One update:** `undo.sh <backup name>`.
- **Privacy steps and services:** their `*-restore.sh` scripts ([list](../devices/s90d/README.md#device-changes)).
- **The bridge:** `devices/s90d/device/ha-bridge/ha-bridge-restore.sh`.
- **Back to Samsung Home:** `recipes/restore-launcher.sh home-01 <HANDLER> <HOME> <STARTUP>`, with the three
  values you wrote down (`com.samsung.tv.csfs` on every TV seen so far). Undo any updates first: it only replaces
  the version `install-launcher.sh` installed. The tested TV was installed by an older script, so it uses
  `devices/s90d/device/launcher-deploy/restore-stock-home.sh` instead.

If the screen is blank after an update, check `/tmp/silentglass-state.txt`, the DLL hash and its labels before
changing anything ([recovery principles](samsung-tizen-notes.md#recovery-principles)).
