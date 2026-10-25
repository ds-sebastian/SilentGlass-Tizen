set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-prime-off.service
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
usr stop $N 2>/dev/null || true
rm -f "$UD/default.target.wants/$N" "$UD/$N"; usr daemon-reload
echo 'PRIME-OFF REMOVED'
