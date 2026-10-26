#!/bin/sh
# Inverse of ha-bridge-install.sh. Retained discovery topics stay on the broker: delete the device in Home Assistant
# (Settings > Devices > Living Room TV > Delete) or clear homeassistant/+/silentglass_s90d/# with an MQTT client.
set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=silentglass-ha.service
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
usr disable $N 2>/dev/null || true; usr stop $N 2>/dev/null || true
rm -f "$UD/default.target.wants/$N" "$UD/$N" $H/.config/silentglass-ha.conf.example; rm -rf /opt/usr/share/silentglass-ha
usr daemon-reload
echo 'HA BRIDGE REMOVED (silentglass-ha.conf kept; delete it if you no longer need the broker credentials on the TV)'
