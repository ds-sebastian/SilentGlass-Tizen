# Inverse of otbr-standby-update.sh (the radio loses power in standby again). Feed to /bin/sh on stdin as root.
set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; Q=/opt/usr/share/quiet-otbr
B=/opt/usr/share/quiet-tizen-backups/thread-border-router
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
usr disable quiet-otbr-usbpower.service 2>/dev/null || true; usr stop quiet-otbr-usbpower.service 2>/dev/null || true
rm -f "$UD/default.target.wants/quiet-otbr-usbpower.service" "$UD/quiet-otbr-usbpower.service" "$Q/quiet-otbr-usbpower.dll" "$Q/quiet-otbr-usbpower.runtimeconfig.json"
[ -f "$B/units-before-usbpower/quiet-otbr-switch.service" ] && cp -a "$B/units-before-usbpower/quiet-otbr-switch.service" "$UD/quiet-otbr-switch.service"
usr daemon-reload
# re-enable Samsung's iot-hub-control if this recipe disabled it
DB=/opt/dbspace/.pkgmgr_parser.db
if [ -e "$B/ihc-disabled-by-quiet-otbr" ]; then
  sqlite3 -bail $DB "BEGIN IMMEDIATE; UPDATE package_app_info SET app_disable='false' WHERE app_id='com.samsung.tv.iot-hub-control' AND app_disable='true'; COMMIT;"
  /usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll reload-owner >/dev/null 2>&1 || true
  rm -f "$B/ihc-disabled-by-quiet-otbr"
fi
# storaged drops the request with the keeper process; the radio powers down at the next standby again
echo "STANDBY POWER REMOVED"
