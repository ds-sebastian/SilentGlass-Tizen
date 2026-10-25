set -eu
H=/opt/usr/home/owner
UD=$H/.config/systemd/user
N=quiet-recog-off.service
ENV="XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus;unix:path=/run/user/5001/dbus/user_bus_socket"
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }  # root may use the user manager private socket
[ "$(id -u)" = 0 ]
usr disable --now $N 2>&1 | tail -n 2 || true
rm -f "$UD/$N" "$UD/default.target.wants/$N"
usr daemon-reload
for U in contents-recognition-service contents-ui-detector-service; do systemctl reset-failed $U 2>/dev/null || true; systemctl start $U; echo "$U: $(systemctl show $U -p ActiveState --value)"; done
echo 'RESTORED: user unit removed, services running'
