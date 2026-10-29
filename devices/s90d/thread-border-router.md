# Thread border router

The S90D has Samsung's full SmartThings hub inside: a Silicon Labs EFR32 radio on `/dev/ttyIOT`, plus Samsung's
builds of `cpcd` and `otbr-agent` (Thread 1.4.0). This recipe runs just the radio and the border router, with the
hub left off.

- **Joins the Thread network you already have:** Home Assistant, Apple, Google or another OTBR. It can also form a
  new one. Samsung's own hub can only make its own network.
- **Keeps routing in standby,** and survives reboots.
- **Has an on/off switch** on the phone page and in Home Assistant. Its status is on the TV's Info panel.
- **Leaves Samsung's system files alone:** everything lives on `/opt`, and `otbr-restore.sh` undoes it. It was
  uninstalled and reinstalled on the tested TV.
- **Side effect:** SmartThings can no longer set up Samsung's own hub on this TV. Your own Matter and Thread devices
  aren't affected.

## 1. Flash the radio (once)

The border router needs the radio on the hub's own firmware image. Check it:

```sh
/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/cpcd \
  -c /opt/usr/apps/com.samsung.tv.iot-hub-control/bin/cpcd_921600.conf -p   # wants "Secondary Protocol v5"
```

A stock TV reports protocol v2: Samsung's factory image, which has no Thread. Flash the hub image with Samsung's
own tool (about 23 s):

```sh
killall zigbeed socat cpcd
/opt/usr/apps/com.samsung.tv.iot-hub-control/bin/ncp-updater -b 115200 -p /dev/ttyIOT -e /dev/ttyIOT -y 1 \
  -f /opt/usr/apps/com.samsung.tizen.smartthings-hub/res/hub/config/hub-core/zigbee/stZigbeeRcp
```

- **Before you flash:** back up `stZigbeeRcp-mfg` and `stZigbeeRcp-latest`, both in `iot-hub-control/bin`.
- **What it writes:** only the radio's app image. The radio's bootloader and the TV's firmware aren't touched.
- **Back to stock:** the same command with `-f .../iot-hub-control/bin/stZigbeeRcp-mfg`.
- **Wrong image:** `stZigbeeRcp-latest` is still protocol v2.
- **Resets:** only `-e /dev/ttyIOT -y 1` resets the radio. Other resets stall at "XMODEM did not start".
- **Boot:** the image stays across reboots, so don't flash at boot.

## 2. Install

Feed `device/thread-border-router/otbr-install.sh` to a root shell.

**Before installing,** it refuses if the hub is on or the units already exist, and backs up the saved network,
labels and network settings.

**Then it:**
- copies Samsung's `otbr-agent` with the network capabilities it needs; it keeps its label, so only the hub app can
  run it;
- disables `iot-hub-control`, which cuts the radio's power in standby;
- adds these services, run as the TV's normal user:

| Service | Does |
|---|---|
| `quiet-otbr-cpcd` | the radio daemon, started after Samsung's boot-time radio check |
| `quiet-otbr-agent` | the border router, on the wired or Wi-Fi network |
| `quiet-otbr-forward` | turns on routing between your network and Thread (via Samsung's own `matter-setup`) |
| `quiet-otbr-usbpower` | keeps the USB port powered in standby (the radio is a USB device) |
| `quiet-otbr-switch` | applies the on/off switch at once |

A typical boot: radio ~15 s, border router ~19 s, router role within ~2 min. Log: `/run/user/5001/quiet-otbr.log`.

## 3. Join your network (once)

Easiest: on the phone page, under **Thread border router**, import Home Assistant's network.

By hand: feed `otbr-join.sh` to a root shell, with one of these as the first line, from a private file:

| First line | Use when |
|---|---|
| `DATASET=<hex>` | you have a network. Get it with `pi/thread_dataset.py` (Home Assistant) or `ot-ctl dataset active -x`. |
| `NEW=1` | you have none; the TV forms one. Add it to Home Assistant afterwards. |
| `FORCE=1` (extra line) | replace a network the TV is already on |

The dataset is your network key: never commit or print it. Don't add a prefix. The TV becomes a router within ~2
min and rejoins by itself after every restart.

## 4. Check it

- **On the TV:** `ot-ctl state` says `router`, and `/proc/sys/net/ipv6/conf/all/forwarding` is 1.
- **On another border router:** `ot-ctl router table` lists the TV.
- **Real routing:** [pi/probe_forwarding.py](pi/probe_forwarding.py) sends one packet through the TV. Watch the
  `OTBR_FORWARD_CHAIN` counter rise. `ot-ctl ping` only proves the TV is in the mesh, not that it routes.

## Updating an older install

- On/off switch: `otbr-switch-update.sh` (undo `otbr-switch-restore.sh`).
- Standby fix: `otbr-standby-update.sh` (undo `otbr-standby-restore.sh`).

New installs include both.

## Undo

`otbr-restore.sh` removes the services and the agent copy, resets forwarding, and restores Samsung's labels. It
keeps the saved network and the radio firmware; see step 1 to flash stock firmware back.

## How it works (for developers)

- **The boot-time factory test:** Samsung's boot setup runs `zigbee-test-app INIT_FACTORY` whenever
  `/tmp/iot_factory_data` is missing. That test does `killall cpcd`, and on the hub image it never passes. So the
  cpcd service recreates the marker first.
- **Forwarding:** Samsung's root `matter-ip-forward.sh` turns on IPv6 forwarding and `OTBR_FORWARD_CHAIN` when
  `/tmp/matter_setup` is touched. Its watcher ignores a file created in the first ~40 s of boot, so the service waits
  for the watcher, then touches the file every 10 s until forwarding is on.
- **Waiting:** every wait is inside `ExecStart` of a `Type=simple` service. A blocking `ExecStartPre` would hold
  the whole user session.
- **Standby,** two causes:
  - `iot-hub-control` switches the radio off unless the hub is on;
  - `storaged` powers USB down unless a running process asked it not to (`UsbDeviceChangePortPower`, dropped when
    that process exits). `quiet-otbr-usbpower` holds that request.
- **The switch:** it writes a `thread-off` marker in SilentGlass's data folder. Every daemon has
  `ConditionPathExists=!…/thread-off`, so off survives reboots. Off leaves IPv6 forwarding on until the next
  reboot.

**Pitfalls:**
- A root `cpcd -p` probe leaves a root-owned `/dev/shm/cpcd`, and the normal cpcd then fails with "Permission
  denied". Probe with the services stopped, or delete it afterwards.
- Don't touch `/tmp/matter_setup` before the marker exists. It reruns the factory test, which kills cpcd.
- The agent hangs on a normal stop once the radio link is gone. `otbr-restore.sh` kills it.
- `ipset: command not found` in the log is harmless.
- Don't mix the test-tree `cpcd` in `iot-hub-control/bin` (protocol v2, `libcpc.so.2`) with the hub agent
  (`libcpc.so.3`).
- Samsung's own `openthread-borderrouter.service` never starts with the hub off, and it isn't used.
