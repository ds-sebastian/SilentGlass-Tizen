#!/bin/sh
set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-csfs-off.service
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
usr disable $N 2>/dev/null || true; usr stop $N 2>/dev/null || true
rm -f "$UD/default.target.wants/$N" "$UD/$N"; usr daemon-reload
echo 'CSFS WATCHER REMOVED (Samsung Home is left running again)'
