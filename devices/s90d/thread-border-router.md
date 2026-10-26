# S90D as a Thread border router on the home network (no SmartThings)

The S90D ships the whole SmartThings hub: a Silicon Labs EFR32 radio behind a CP2102N USB-UART
(`/dev/ttyIOT`), Samsung's builds of `cpcd` and `otbr-agent` (Silicon Labs 4.4.6-1, Thread 1.4.0), Zigbee
and the hub runtime. With the hub off (`db/iot/thing/iot_mode` = 0) none of it runs. This recipe runs only
the radio daemon and the border router, joins the **existing** home Thread network, and keeps the hub off.
Samsung's system files are not changed; everything lives on the writable `/opt` volume and is undone by
`device/thread-border-router/otbr-restore.sh`.

It works with any Thread network: join the one you have (Home Assistant, Apple, Google, another OTBR - step 3),
or let the TV form a new one if you have none. Samsung's own SmartThings hub can only create its own network.

Status on the tested TV (firmware in [README.md](README.md)): router in the home partition beside an
upstream OTBR, LAN -> Thread forwarding proven with a real packet, survives reboots (verified across
several owner reboots). Standby/wake is not yet confirmed: a 45-minute watch saw it keep routing and the kernel
never suspend, but whether the TV was switched off during it was not recorded. Thread status
is on SilentGlass Home's **Info** tab ("Home network: Connected / Forwarding off"), with a switch to stop and start it
(section 5).

## 1. Radio firmware (once, owner approval)

The border router needs the radio on the hub's own image (CPC protocol v5). Check:

```sh
/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/cpcd \
  -c /opt/usr/apps/com.samsung.tv.iot-hub-control/bin/cpcd_921600.conf -p   # wants "Secondary Protocol v5"
```

A stock TV that never enabled the hub reports protocol v2 at 115200 (`stZigbeeRcp-mfg`, Samsung's
manufacturing image). That image has no Thread endpoint, and the hub `cpcd` refuses it ("Secondary Protocol v2
is not supported"). Switch it with Samsung's own flasher and image, exactly as `zigbee-test-app` invokes it:

```sh
killall zigbeed socat cpcd
/opt/usr/apps/com.samsung.tv.iot-hub-control/bin/ncp-updater -b 115200 -p /dev/ttyIOT -e /dev/ttyIOT -y 1 \
  -f /opt/usr/apps/com.samsung.tizen.smartthings-hub/res/hub/config/hub-core/zigbee/stZigbeeRcp
# ~23 s, ends "Firmware Update Success"
```

- This writes only the radio module's application image through its Gecko bootloader; the bootloader and the
  TV firmware are untouched. Back up both shipped images first (`stZigbeeRcp-mfg`, `stZigbeeRcp-latest` in
  `iot-hub-control/bin`) and keep their hashes.
- `-e /dev/ttyIOT -y 1` resets the radio through the `iotcontrol` module. Sysfs GPIO resets, `cpcd -f`, or
  other bauds/flow-control settings all stall at "XMODEM did not start".
- `stZigbeeRcp-latest` is not the right image (protocol v2 at 921600).
- Back to stock: the same command with `-f .../iot-hub-control/bin/stZigbeeRcp-mfg`.
- The v5 image stayed in place across every reboot; do not add reflash logic to boot.

## 2. Install the units

Feed `device/thread-border-router/otbr-install.sh` to `/bin/sh` on stdin as root (unsigned script files are
refused under UEP). It refuses if the units exist or the hub is enabled, makes a first-run backup
(`/opt/usr/share/quiet-tizen-backups/thread-border-router/`: saved-network files and labels, sysctls,
iptables) and then:

1. copies Samsung's `otbr-agent` byte-for-byte to `/opt/usr/share/quiet-otbr/otbr-agent` (sha256 checked)
   with `cap_dac_override,cap_net_admin,cap_net_raw=ep`. The vendor binary keeps `=ei` (which only works
   under Samsung's system unit). The copy keeps the package label
   `User::Pkg::com.samsung.tizen.smartthings-hub::RO`, which only the hub app, `User`, `System` and
   `System::Privileged` may execute (checked in `/sys/fs/smackfs/load2`), so other apps cannot use it;
2. relabels `/opt/var/lib/thread` `User` and gives its `.data` files to `owner` (modes stay 0600);
3. stores the factory-data marker (`/tmp/iot_factory_data` content, or the radio EUI-64 reversed + `fw_version`);
4. installs and starts three **owner user units** (`~owner/.config/systemd/user`, label `User::Home`,
   enabled via `default.target.wants`; the same mechanism as `quiet-recog-off`):

| Unit | Does |
|---|---|
| `quiet-otbr-cpcd` | waits until Samsung's boot-time `iot-hub-control-setup.service` has run and finished, recreates `/tmp/iot_factory_data`, runs the hub `cpcd` (921600, socket `/dev/shm/cpcd/cpcd_0`) |
| `quiet-otbr-agent` | waits for the cpcd socket, runs the private agent `-s -B <backbone> -d 4 "spinel+cpc://cpcd_0?iid=2"` (auto-attaches to the saved network); the backbone is the default-route interface (`eth0` wired, `wlan0` Wi-Fi), waited for up to 2 min |
| `quiet-otbr-forward` | waits for `wpan0` and Samsung's `matter-setup.path`, then touches `/tmp/matter_setup` every 10 s until IPv6 forwarding is on |
| `quiet-otbr-switch.path` / `.service` | SilentGlass Home's Info switch (section 5): applies a flip at once |

Why each wait exists:

- **Factory test**: at every boot Samsung's setup runs `zigbee-test-app INIT_FACTORY`, which does
  `killall cpcd`, whenever `/tmp/iot_factory_data` is missing. On the v5 image the test can never pass
  (it times out ~90 s into boot), so without the marker every later setup run kills the radio daemon.
- **Forwarding**: Samsung's root `matter-ip-forward.sh` (behind `matter-setup.path`) sets IPv6 forwarding,
  `accept_ra=2` and `OTBR_FORWARD_CHAIN` (eth0/wlan0 <-> wpan0, FORWARD policy DROP). The owner can trigger it
  by touching `/tmp/matter_setup`, but the watcher only starts ~40 s into boot and ignores a file created
  before that, so the unit waits for it and re-touches instead of waiting for the file to be consumed.
- Waiting happens inside `ExecStart` of `Type=simple` units: the owner `default.target` is ordered after its
  wants, so a blocking `ExecStartPre` or oneshot would hold the user session.

Log: `/run/user/5001/quiet-otbr.log`. Typical boot: cpcd ~15 s, agent ~19 s, router within ~2 min,
forwarding once Samsung's watcher is up.

## 3. Join a network (once)

`device/thread-border-router/otbr-join.sh`, fed to `/bin/sh` on stdin as root, with the choice on its first line
from a private file (never commit or print a dataset - it is the network key):

| First line | Use when |
|---|---|
| `DATASET=<hex>` | you have a Thread network. From Home Assistant: `HA_URL=... HA_TOKEN=... python3 pi/thread_dataset.py ~/.config/quiet-tizen/thread.env` saves HA's preferred network (mode 600). From any OTBR: `ot-ctl dataset active -x`. |
| `NEW=1` | you have none: the TV forms one. Add it to Home Assistant afterwards (Thread integration). |
| `FORCE=1` (extra line) | the TV is already attached and you want to replace that network |

```sh
cat ~/.config/quiet-tizen/thread.env device/thread-border-router/otbr-join.sh | <root shell on the TV>
```

It refuses to replace an attached network without `FORCE=1` and never echoes the dataset. Do not add a prefix:
the TV favors the existing OMR prefix. It attaches as a child and becomes a router within ~2 minutes. The network
is saved in `/opt/var/lib/thread`, so every later start (boot or the Info switch) rejoins by itself.

## 4. Verify (mesh membership is not border routing)

- TV: units active with `NRestarts=0`, `ot-ctl state` = `router`, `/proc/sys/net/ipv6/conf/all/forwarding` = 1.
- Existing OTBR: `ot-ctl router table` lists the TV's extended MAC.
- Real forwarding: run [pi/probe_forwarding.py](pi/probe_forwarding.py) on a LAN host (frame to the TV MAC,
  destination a Thread address of the other border router) and watch the TV's
  `ip6tables -nvx -L OTBR_FORWARD_CHAIN` eth0 -> wpan0 counter rise. `ot-ctl ping` from the TV only proves
  mesh reachability.

## 5. Info page switch (SilentGlass Home)

SilentGlass Home's Info page shows **Thread border router** with a switch, only on TVs where these units are installed.

- Off: SilentGlass Home creates `thread-off` in its own data directory
  (`/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data`, persistent; the owner's systemd may read it)
  and rewrites `thread-switch`. `quiet-otbr-switch.path` sees the rewrite and stops forward, agent, then cpcd
  (under 2 s). The TV leaves the mesh as a router; other border routers carry it.
- On: SilentGlass Home deletes the marker and rewrites `thread-switch`; the units start and rejoin the saved network
  (router again in ~10 s when the mesh still remembers it, up to ~2 min otherwise).
- Reboots: every daemon has `ConditionPathExists=!.../thread-off`, so the choice holds at boot and a manual
  start while off is skipped (a skipped condition never triggers `Restart=`).
- Off leaves IPv6 forwarding on until the next reboot (Samsung's root script set it; with no `wpan0` nothing is
  routed).

Upgrading an older install of this recipe: `otbr-switch-update.sh` (keeps the previous units in the backup folder;
running daemons are not restarted); inverse `otbr-switch-restore.sh` (always-on units, border router running).

## Pitfalls

- A root `cpcd -p` probe leaves a root-owned `/dev/shm/cpcd`; the owner `cpcd` then dies with
  `server_core_init ... Permission denied` until it is removed. Probe with the units stopped, or remove it after.
- Do not touch `/tmp/matter_setup` before the marker exists (it re-runs the factory test, which kills cpcd).
- The agent hangs on a normal stop once CPC is gone; `otbr-restore.sh` kills it.
- `ipset: command not found` in the log is harmless; it does not mean forwarding is handled.
- The test-tree `cpcd` in `iot-hub-control/bin` speaks only protocol v2 and pairs with `libcpc.so.2`; do not
  mix it with the hub `otbr-agent` (`libcpc.so.3`).
- Samsung's own `openthread-borderrouter.service` never starts with the hub off (`iot_mode` gate) and uses
  `-B wlan0`; it is not used.

## Undo

`device/thread-border-router/otbr-restore.sh`: removes the units (including the switch) and the private agent, kills only their
processes, resets forwarding and `OTBR_FORWARD_CHAIN`, and restores Samsung's labels/owners on the saved network
and the vendor agent's `=ei`. It keeps the saved network and the radio firmware (see step 1 for stock firmware).
Tested as a full uninstall/reinstall round trip on the TV.
