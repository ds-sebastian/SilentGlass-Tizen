# Install SilentGlass

For an owner who already has root on a compatible Samsung Tizen TV (developed on a QN65S90D, Tizen, .NET 6). Every
step is a separate, reversible phase; do them in order, check each one on the screen, and keep the backups.
Root access itself is not part of this repository: this project uses the reviewed external
[s90c-tools](https://github.com/invaliddayta/s90c-tools) `tv-access` helper - see the
[agent walkthrough](agent-workflow.md#2-confirm-the-existing-access-path).

Scripts are fed to a root `/bin/sh` on **stdin** (`/bin/sh -s -- ARGS < script`): the TV's UEP refuses unsigned
script files. Files are staged in `/home/owner/share/tmp/sdk_tools/quiet-tizen/` (e.g. with `sdb push`).

## 1. Build

SilentGlass is a native NUI/.NET app. Build it against the TV's own framework assemblies - pull these four from the
TV (`find /usr/share/dotnet.tizen -name 'Tizen.NUI.dll' -o -name 'Tizen.Applications.Common.dll' -o -name
'Tizen.System.dll' -o -name 'Tizen.dll'`) into `.local/tv-framework/`. They are Samsung's and never committed; the
project files reference them with `Private=false`.

```sh
python3 tools/build.py launcher --framework .local/tv-framework
dotnet build devices/s90d/device/ha-bridge/src/HaBridge.csproj -c Release -o .local/ha-bridge
python3 tools/bridge_installer.py .local/ha-bridge     # embeds the build into ha-bridge-install.sh
```

`.local/package/` then holds `BixbyPromotion.dll` (SilentGlass), `QuickLaunch.dll` (settings-key bridge),
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

## 4. Settings keys (optional)

Routes the remote's settings keys (`XF86More` on QuickLaunch, `XF86SysMenu` on `org.tizen.menu`) to the Controls
panel and replaces QuickLaunch with a tiny bridge that answers the vendor's preload requests and exits.

```sh
/bin/sh -s -- shell-01 < recipes/snapshot.sh
/bin/sh -s -- shell-01 < recipes/install-settings-bridge.sh
```

## 5. Home Assistant bridge (optional)

```sh
/bin/sh < devices/s90d/device/ha-bridge/ha-bridge-install.sh
```

It installs as an owner service and starts unconfigured. On the TV: **Home > Info > Home Assistant** shows a QR code;
scan it with a phone on the same network, enter the code, and fill in your MQTT broker (the Test button checks it),
the TV's name, notifications and - if you use the border router - your Thread network. The settings are stored on the
TV only (owner-readable). Details: [Home Assistant bridge](../devices/s90d/ha-bridge.md).

## 6. Thread border router and privacy batches (optional)

- [Thread border router](../devices/s90d/thread-border-router.md): radio firmware, units, joining your network
  (the phone setup page can do the joining).
- Privacy batches and boot units: the [S90D record](../devices/s90d/README.md) lists each phase with its inverse.
  What the targeted services do is in [Tizen notes](samsung-tizen-notes.md).

## Undo

Each phase has its inverse; run them in reverse order:

- `devices/s90d/device/ha-bridge/ha-bridge-restore.sh`
- `recipes/restore-settings-bridge.sh shell-01`
- `recipes/restore-launcher.sh home-01 ORIGINAL_MAIN_OPERATION_ID ORIGINAL_HOME_ID ORIGINAL_STARTUP_ID` - the three
  values you saved (all `com.samsung.tv.csfs` on the TVs seen so far; use yours)

If the screen is blank after an update, read `/tmp/silentglass-state.txt`, the installed DLL hash, its labels and
any `.native_image` cache before changing anything else. See [recovery principles](samsung-tizen-notes.md#recovery-principles).
