# Install SilentGlass

For an owner who already has root on a compatible Samsung Tizen TV (developed on a QN65S90D, Tizen, .NET 6). Every
step is a separate phase with its own undo script; do them in order, check each one on the screen, and copy the
backups off the TV. Read [Risks](../README.md#risks) first: most undo scripts have not
been run on a TV yet.
Root access itself is not part of this repository: this project uses the reviewed external
[s90c-tools](https://github.com/invaliddayta/s90c-tools) `tv-access` helper - see the
[agent walkthrough](agent-workflow.md#2-confirm-the-existing-access-path).

Scripts are fed to a root `/bin/sh` on **stdin** (`/bin/sh -s -- ARGS < script`): the TV's UEP refuses unsigned
script files. Files are staged in `/home/owner/share/tmp/sdk_tools/quiet-tizen/` (e.g. with `sdb push`).

## 1. Get the files

**From a release** (no build needed): download `silentglass-<version>.tar.gz`, `ha-bridge-install.sh` and
`SHA256SUMS` from the release page, check them with `sha256sum -c SHA256SUMS`, and stage the tarball as
`quiet-tizen/package.tar.gz` (step 2). The package holds only this project's code and assets: no Samsung files, no
photo wallpapers (it uses the built-in background unless you add `wallpapers/wall-day.jpg` and `wall-night.jpg`).

**Or build it yourself.** SilentGlass is a native NUI/.NET app. Build it against the TV's own framework assemblies - pull these four from the
TV (`find /usr/share/dotnet.tizen -name 'Tizen.NUI.dll' -o -name 'Tizen.Applications.Common.dll' -o -name
'Tizen.System.dll' -o -name 'Tizen.dll'`) into `.local/tv-framework/`. They are Samsung's and never committed; the
project files reference them with `Private=false`.

```sh
python3 tools/build.py launcher --framework .local/tv-framework
python3 tools/build.py bridge
python3 tools/bridge_installer.py .local/build/ha-bridge   # embeds the build into ha-bridge-install.sh
```

Builds are reproducible with the pinned SDK (`global.json`, .NET SDK 6.0.428): the same commit gives the same
bytes, so you can compare your build with a release's `SHA256SUMS`. `tools/release.py VERSION` makes the release
files from a clean checkout.

`.local/package/` then holds `BixbyPromotion.dll` (SilentGlass),
`Probe.dll` (Home-handler tool) and the assets. Your TV's apps for the Apps menu come from
`launcher/assets/apps.local.json` if present (see [app library](app-library.md)).

## 2. Install the launcher (Home unchanged)

SilentGlass replaces the entry assembly of Samsung's **`com.samsung.tv.bixby-promotion`** container, keeping the
package's identity, privileges (including notification windows) and labels. It does not change Home yet.

```sh
/bin/sh -s -- home-01 < recipes/snapshot.sh        # registry, preferences and routing backup
tar -czf .local/package.tar.gz -C .local/package . # stage as quiet-tizen/package.tar.gz on the TV
/bin/sh -s -- home-01 < recipes/install-launcher.sh
```

The recipe backs up the original container to `/opt/usr/share/quiet-tizen-backups/home-01/`, moves the stale native
image aside, installs the assembly and resources with the package's labels and launches it once. Check: Home opens
the bar; Watch, Play and Inputs switch inputs; Controls changes brightness and volume; Info opens.

## 3. Make it Home

Save the three original values first (in `home-01/operation-and-sources-before.txt` and `preferences-before.txt`):
the main-operation Home handler, `db/appfw/home_appid` and `db/appfw/startup_appid`. Then:

```sh
/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/home-01/Probe.dll set-home com.samsung.tv.bixby-promotion
vconftool set -f -t string db/appfw/home_appid com.samsung.tv.bixby-promotion
vconftool set -f -t string db/appfw/startup_appid org.tizen.tv-viewer   # power on to the last input
```

Read all three back, restart with the remote, and check full boot, Home, standby/wake and return from an app.

## 4. Home Assistant bridge (optional)

```sh
/bin/sh < devices/s90d/device/ha-bridge/ha-bridge-install.sh
```

It installs as an owner service and starts unconfigured. On the TV: **Home > Info > Advanced settings** shows a QR code;
scan it with a phone on the same network, enter the code, and fill in your MQTT broker (the Test button checks it),
the TV's name, notifications and - if you use the border router - your Thread network. The settings are stored on the
TV only (owner-readable). Details: [Home Assistant bridge](../devices/s90d/ha-bridge.md).

## 5. Thread border router and privacy batches (optional)

- **Stop the silent updates first** (`devices/s90d/device/cleanup/batchg-apply.sh`, inverse `batchg-restore.sh`).
  Turning off "Auto update" in the TV's menus is not enough: Samsung's built-in app upgrader has its own switch, and
  an update reinstalls an app enabled, undoing a privacy batch. Batch G turns that switch and the second firmware
  auto-update path off; manual updates still work. If an app comes back anyway, Info names it, and
  `cleanup/reapply-apply.sh` disables everything on the privacy list again.
- [Thread border router](../devices/s90d/thread-border-router.md): radio firmware, units, joining your network
  (the phone setup page can do the joining).
- Privacy batches and boot units: the [S90D record](../devices/s90d/README.md) lists each phase with its inverse.
  What the targeted services do is in [Tizen notes](samsung-tizen-notes.md).

## Undo

Each phase has its inverse; run them in reverse order (newest change first), and the launcher last:

- the privacy batches and boot services: their `*-restore.sh` scripts (listed in the [S90D record](../devices/s90d/README.md))
- `devices/s90d/device/ha-bridge/ha-bridge-restore.sh`
- back to Samsung Home: `recipes/restore-launcher.sh home-01 ORIGINAL_MAIN_OPERATION_ID ORIGINAL_HOME_ID
  ORIGINAL_STARTUP_ID` - the three values you saved (all `com.samsung.tv.csfs` on the TVs seen so far; use yours).
  It only replaces the version `install-launcher.sh` put there (it checks that DLL's hash); after an update, run
  that update's inverse first.
  The tested TV was installed by an earlier trial script; its undo is
  `devices/s90d/device/launcher-deploy/restore-stock-home.sh`, which works from any SilentGlass version.

If the screen is blank after an update, read `/tmp/silentglass-state.txt`, the installed DLL hash, its labels and
any `.native_image` cache before changing anything else. See [recovery principles](samsung-tizen-notes.md#recovery-principles).
