set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; QD=$H/.config/quiet-home; N=quiet-prime-off.service; BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ -f $BK/$N.v5-before ]
cp -a $BK/$N.v5-before "$UD/$N"; mv $BK/$N.v5-before $BK/$N.v5-before.restored-$(date +%Y%m%dT%H%M%S)
rm -f "$QD/prime-off.sh"; rmdir "$QD" 2>/dev/null || true
usr daemon-reload; usr restart $N; echo "active=$(usr is-active $N)"; echo 'JANITOR v4 RESTORED'
