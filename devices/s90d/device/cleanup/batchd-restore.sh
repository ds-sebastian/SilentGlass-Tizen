#!/bin/sh
# Inverse of batchd-apply.sh for the reversible parts. Uninstalled store apps (Prime Video, Apple TV, Internet browser)
# come back from the Samsung Apps store.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchd; DB=/opt/dbspace/.pkgmgr_parser.db
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-prime-off.service
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ -d $BK ]
sqlite3 -bail $DB "BEGIN IMMEDIATE; UPDATE package_app_info SET app_disable='false' WHERE app_id IN ('com.samsung.tv.home.media','com.samsung.tv.cobalt-yt') AND app_disable='true'; COMMIT;"
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll reload-owner
if [ -f $BK/$N ]; then
  mkdir -p $H/.config/quiet-home "$UD/default.target.wants"
  cp -a $BK/$N "$UD/$N"; [ -f $BK/prime-off.sh ] && cp -a $BK/prime-off.sh $H/.config/quiet-home/prime-off.sh
  chown owner:users $H/.config/quiet-home; chsmack -a User::Home $H/.config/quiet-home
  ln -sf "$(cat $BK/wants-link.txt 2>/dev/null || echo $UD/$N)" "$UD/default.target.wants/$N"
  usr daemon-reload; usr start $N; echo "janitor: $(usr is-active $N)"
fi
mv $BK $BK.restored-$(date +%Y%m%dT%H%M%S)
echo 'BATCH D RESTORED (media rows + TV YouTube enabled again; reinstall Prime/Apple TV/browser from the store if wanted)'
