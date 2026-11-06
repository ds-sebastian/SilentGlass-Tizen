# Internet switch (optional)

Blocks the TV's Internet traffic: Samsung cloud, SmartThings, ads, analytics and updates. The home network keeps
working: Home Assistant, notifications, Thread/Matter, the phone page and computer access. Your streaming box is a
separate device and isn't affected. It's a **privacy and update kill switch, not a speed boost**: in a standby
trial, CPU and memory didn't change, and Samsung's apps kept retrying about 3 times a second.

## Use it
- TV: Info › **Internet access**. Phone page: **Privacy and updates**. Home Assistant: the **Internet access**
  switch, with **Internet filter** as its status.
- Status: **Blocked** (verified this boot, within 20 min), **Allowed**, **Applying**, **Unverified** or **Error**.
  Only Blocked means the filter was checked.

## How it works
1. The switch writes `internet-off` in SilentGlass's data folder (present = off).
2. The bridge publishes `<boot id>|on/off` over MQTT. That's only a wake-up: the controller never takes commands
   from MQTT.
3. `pi/internet_controller.py` (on an always-on computer with `tv-access`) runs the fixed `device/internet/firewall.sh`
   as root. It reads the marker on the TV, adds or removes the chains, checks every rule in IPv4 and IPv6, and writes
   `internet-state.txt` (boot id, uptime, result).
4. While blocked it re-checks every 10 min and repairs drift. A TV reboot (new boot id) triggers a reapply.

Rules: `SG_WAN_OUT`/`SG_WAN_IN` first in OUTPUT/INPUT. They let through loopback, RFC1918, link-local, multicast and
broadcast (IPv6: ::1, fe80::/10, fc00::/7, ff00::/8), then REJECT everything else (tcp-reset for TCP, so apps fail
fast). FORWARD (Thread routing) and Samsung's `STC_*`/`DPM-*` chains are never touched.

## Limits
- **Not fail-closed.** After a TV reboot the Internet is open until the bridge reconnects and the controller
  reapplies the block (about a minute). It also needs that computer running. For a hard guarantee, block the TV at
  your router.
- **Why a computer, not the TV:** an owner service can't change the firewall. A copy of `xtables-multi` with file
  capabilities is refused at exec by Samsung's signature check (`[Verification fail] ... is not signed`), even with
  `cp -a`. `stc-iptables` only answers root/`network_fw`.
- The clock syncs from Samsung (`time.samsungcloudsolution.*`) and drifts slowly while blocked.

## Install
1. TV (root, stdin): `device/internet/prepare.sh`. It backs up the rule lists and creates the `internet-controller`
   marker that shows the switch.
2. Computer: `uv venv && uv pip install paho-mqtt==2.1.0`, write a `chmod 600` JSON config (`host`, `port`, `user`,
   `password`, `tv_access`, `tv_ip`, `script`), and install `pi/silentglass-internet.service.in` as a user unit.

## Undo
1. Turn the switch on (or run `firewall.sh allow` as root): our chains are removed.
2. Stop and disable the controller.
3. Delete `internet-controller` from the data folder: the switch disappears.

`allow` was run on the TV in the offline tests only; the on/off round trip through the switch ran on the TV on
2026-11-06.
