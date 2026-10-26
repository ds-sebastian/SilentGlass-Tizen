# Undo otbr-install.sh. Feed to /bin/sh on stdin as root.
# Keeps the saved Thread network (credentials) and the radio firmware; see the doc for returning the radio to stock.
set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; Q=/opt/usr/share/quiet-otbr
HUB=/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin; T=/opt/var/lib/thread
B=/opt/usr/share/quiet-tizen-backups/thread-border-router
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
# forwarding back to the pre-install values while wpan0 still exists (a reboot clears it too)
if [ -f "$B/sysctl-before.txt" ]; then sysctl -p "$B/sysctl-before.txt" >/dev/null 2>&1 || true
else sysctl -w net.ipv4.ip_forward=0 net.ipv6.conf.all.forwarding=0 >/dev/null; fi  # TV defaults
for X in iptables ip6tables; do "$X" -D FORWARD -j OTBR_FORWARD_CHAIN 2>/dev/null || true; "$X" -F OTBR_FORWARD_CHAIN 2>/dev/null || true; "$X" -X OTBR_FORWARD_CHAIN 2>/dev/null || true; "$X" -P FORWARD ACCEPT; done
for n in quiet-otbr-switch.path quiet-otbr-switch.service quiet-otbr-forward.service quiet-otbr-agent.service quiet-otbr-cpcd.service; do
  usr disable "$n" 2>/dev/null || true
  usr kill --kill-who=all --signal=SIGKILL "$n" 2>/dev/null || true  # the agent hangs on a normal stop once CPC is gone
  usr stop --no-block "$n" 2>/dev/null || true
  rm -f "$UD/default.target.wants/$n" "$UD/$n"
done
usr daemon-reload
rm -rf /dev/shm/cpcd
# Samsung's permissions on the saved network and its agent
for f in "$T"/*.data; do [ -f "$f" ] || continue; chown root:root "$f"; chmod 600 "$f"; chsmack -a User "$f"; done
[ -d "$T" ] && chsmack -a System "$T"
setcap cap_dac_override,cap_net_admin,cap_net_raw=ei "$HUB/otbr-agent"
rm -rf "$Q"
rm -f /opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data/thread-off /opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data/thread-switch
echo "RESTORED: units removed, forwarding reset, Samsung permissions back; backup kept in $B"
