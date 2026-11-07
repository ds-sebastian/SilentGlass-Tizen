# Internet switch

Cuts the TV off from the Internet: Samsung cloud, SmartThings, ads, analytics and updates. The home network keeps
working: Home Assistant, notifications, Thread/Matter, the phone page and computer access. Your streaming box is a
separate device and isn't affected. It's a **privacy and update kill switch, not a speed boost**: in a standby
trial, CPU and memory didn't change, and Samsung's apps kept retrying about 3 times a second.

## Use it
- TV: Info › **Outside internet**. Phone page: **Privacy and updates › Outside internet**. Home Assistant: the
  **Outside internet** switch, with **Outside internet status**. Off blocks only the outside Internet; the home
  network stays on.
- Status, read from the TV's own routes: **Blocked**, **Allowed**, **Applying** (off, but a gateway route is still
  there), **IPv6 open** (off, but the TV has a public IPv6 address and route) or **No connection**.
- Stuck? On the TV: Settings › General › Network › Network Status › IP Settings › Obtain automatically.

## How it works
1. The switch writes `internet-off` in SilentGlass's data folder (present = off).
2. The bridge watches that file and asks connman (the TV's network manager) over D-Bus to set the connected network
   to manual IPv4: **the same address and netmask, no gateway**. The owner user is allowed to do this; it's the same
   call as Samsung's IP Settings menu. Before the first change it saves what it replaced in `internet-applied.txt`.
3. Switch on: the bridge puts back exactly what was there (DHCP, or your own manual settings) and deletes the record.
4. connman saves the setting, so it holds through restarts and power cuts with no gap. The bridge re-checks every
   minute.

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
- The clock syncs from Samsung (`time.samsungcloudsolution.*`) and drifts slowly while blocked.
- Why not a firewall: the owner user can't change iptables. A copy of `xtables-multi` with file capabilities is
  refused at exec by Samsung's signature check, and `stc-iptables` only answers root/`network_fw`.

## Undo
Turn the switch on, or use the TV menu above. Removing SilentGlass with the switch off leaves the TV on manual
IPv4 without a gateway; set it to Obtain automatically in that menu.
