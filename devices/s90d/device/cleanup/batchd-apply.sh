#!/bin/sh
# Batch D (2026-10-25, owner request "SHIELD for everything"): remove the janitor unit; disable Samsung Home's media rows
# and TV YouTube (app_disable); uninstall Prime Video, Apple TV, web browser (store apps). Inverse: batchd-restore.sh
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchd; DB=/opt/dbspace/.pkgmgr_parser.db
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-prime-off.service
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
[ ! -e $BK ] || { echo 'batch D already applied - refusing'; exit 3; }
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
[ "$(sqlite3 -readonly $DB "select count(*) from package_app_info where app_id in ('com.samsung.tv.home.media','com.samsung.tv.cobalt-yt') and app_disable='false';")" = 2 ] || { echo 'disable targets not in expected state - refusing'; exit 4; }
mkdir -m 700 $BK
cp -a $DB $BK/registry-before-batchd.db; sha256sum $DB $BK/registry-before-batchd.db
for p in org.tizen.primevideo com.samsung.tv.aria-video org.tizen.browser; do for f in /opt/share/packages/$p.xml /usr/share/packages/$p.xml; do [ -f $f ] && cp -a $f $BK/; done; done

echo '=== 1. janitor unit off'
usr stop $N 2>/dev/null || true
cp -a "$UD/$N" $BK/; [ -f $H/.config/quiet-home/prime-off.sh ] && cp -a $H/.config/quiet-home/prime-off.sh $BK/
[ -L "$UD/default.target.wants/$N" ] && readlink "$UD/default.target.wants/$N" > $BK/wants-link.txt
rm -f "$UD/default.target.wants/$N" "$UD/$N" $H/.config/quiet-home/prime-off.sh; rmdir $H/.config/quiet-home 2>/dev/null || true
usr daemon-reload; echo "janitor: $(usr is-enabled $N 2>&1 | tail -n1)"

echo '=== 2. disable media rows + TV YouTube'
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
UPDATE package_app_info SET app_disable='true' WHERE app_id IN ('com.samsung.tv.home.media','com.samsung.tv.cobalt-yt') AND app_disable='false';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=2 FROM package_app_info WHERE app_id IN ('com.samsung.tv.home.media','com.samsung.tv.cobalt-yt') AND app_disable='true';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner; $HELPER read-owner com.samsung.tv.home.media com.samsung.tv.cobalt-yt
for a in com.samsung.tv.home.media com.samsung.tv.cobalt-yt; do app_launcher -u 5001 -k $a >/dev/null 2>&1 || true; done

echo '=== 3. uninstall Prime Video, Apple TV, browser'
for p in org.tizen.primevideo com.samsung.tv.aria-video org.tizen.browser; do
  app_launcher -u 5001 -k $p >/dev/null 2>&1 || true
  echo "-- $p: $(pkgcmd -u -n $p 2>&1 | tail -n 1)"
done
sleep 3
echo "left in registry: $(sqlite3 -readonly $DB "select count(*) from package_info where package in ('org.tizen.primevideo','com.samsung.tv.aria-video','org.tizen.browser');")"
app_launcher -S 2>/dev/null | grep -E 'home.media|cobalt|primevideo|aria-video|browser' || echo 'none of the batch D apps running'
grep MemAvailable /proc/meminfo
echo 'BATCH D APPLIED'
