# Home network only (Internet kill switch)

Cuts the TV off from the Internet: Samsung cloud, SmartThings, ads, analytics and updates. The home network keeps
working: Home Assistant, notifications, Thread/Matter, the phone page and computer access. Your streaming box is a
separate device and isn't affected. It's a **privacy and update kill switch, not a speed boost**: in a standby
trial, CPU and memory didn't change, and Samsung's apps kept retrying about 3 times a second.

## Use it
- TV: Info › **Home network only**. Phone page: **Privacy and updates › Home network only**. Home Assistant:
  the **Home network only** switch, with **Internet** as its status. **On** blocks the outside Internet; the home
  network keeps working. Off is normal.
- Status, read from the TV's own routes: **Blocked**, **Allowed**, **Applying** (off, but a gateway route is still
  there), **IPv6 open** (off, but the TV has a public IPv6 address and route) or **No connection**.
- Stuck? On the TV: Settings › General › Network › Network Status › IP Settings › Obtain automatically. The Menu
  key opens Samsung's Settings even with the switch on, and Developer Mode stays reachable too (Controls ›
  **Developer Mode** opens Samsung's Apps; dismiss its Network Error popup, then type 1 2 3 4 5 on the remote).

## How it works
1. Turning it on writes `internet-off` in SilentGlass's data folder (present = off).
2. The bridge watches that file and asks connman (the TV's network manager) over D-Bus to set the connected network
   to manual IPv4: **the same address and netmask, no gateway**. The owner user is allowed to do this; it's the same
   call as Samsung's IP Settings menu. Before the first change it saves what it replaced in `internet-applied.txt`.
3. Turning it off: the bridge puts back exactly what was there (DHCP, or your own manual settings) and deletes the record.
4. connman saves the setting, so it holds through restarts and power cuts with no gap. The bridge re-checks every
   minute.
5. **Home stays openable (1.3.1).** Samsung checks its web-app list before opening the Home app, and SilentGlass's
   slot inherited a "needs network" mark, so with no gateway Home showed Samsung's network error. The bridge watches
   `/opt/dbspace/.webappservice.db` and keeps that mark at 0; Samsung rewrites it on every launch, and it's cleared
   again in under a second.
6. **Fail-safe (1.3.1).** If neither the router nor the Home Assistant broker answers for 10 minutes while the link is
   up (a new router, another subnet), the bridge puts the TV's own network settings back and reports
   **Open · home network lost**. It blocks again once the home network answers. Tested on the TV (2026-11-08) by
   dropping its traffic to the router and the broker: DHCP came back after 10 min 44 s (the checks run every minute),
   and the block returned 7 s after both answered again. A switch-off record from 1.3.0 has no router address, so
   until the switch is turned off once more (or the fail-safe has fired once), only the broker is watched.

Nothing runs off the TV and nothing runs as root. No firewall rules; Thread forwarding and IPv6 are untouched.

## What counts as the home network
There's no allow list. Without a gateway the TV can only reach addresses on its own subnet, as set by the netmask
from DHCP (a /24 here), plus IPv6 link-local and the local ULA/Thread prefixes. Anything else needs the gateway, so
it's out of reach. Devices on other subnets or VLANs, and VPN/Tailscale addresses, are unreachable while off, even
if they're yours. Home Assistant, the MQTT broker and the DNS server must be on the TV's subnet (they are here).

## Limits
- **DHCP lease:** while off, the TV keeps its address but stops renewing the lease. Reserve the TV's address in your
  router (a fixed IP) so nothing else gets it during a long block.
- **IPv6:** the home network's IPv6 is local only (ULA/Thread), so there's nothing to block. If your router ever
  hands out public IPv6, the status shows **IPv6 open**: the TV can reach the Internet over IPv6.
- Samsung's apps keep trying. They broadcast ARP for Internet addresses that nobody answers: a few small packets a
  second on the home network.
- **Clock:** Samsung sets the clock only from its own time server, so after a cold start with the switch on, the
  TV's clock reads 1970. Since 1.3.1 the bridge reads the time from the `Date:` header of the router's (or Home
  Assistant's) web server, and SilentGlass's clocks and day/night wallpaper use it. Samsung's own menus still show
  the wrong time until the switch is off.
- Why not a firewall: the owner user can't change iptables. A copy of `xtables-multi` with file capabilities is
  refused at exec by Samsung's signature check, and `stc-iptables` only answers root/`network_fw`.

## Undo
Turn Home network only off, or use the TV menu above. Removing SilentGlass with Home network only on leaves the TV on manual
IPv4 without a gateway; set it to Obtain automatically in that menu.
