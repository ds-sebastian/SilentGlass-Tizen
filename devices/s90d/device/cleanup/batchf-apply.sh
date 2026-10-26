#!/bin/sh
# Batch F (2026-10-26): Prime Video, Apple TV and the web browser came back ~3.5 h after batch D uninstalled them -
# they are Samsung's country preloads, which the TV reinstalls. Disabled apps stay disabled, so keep them installed
# and disabled (app_disable), like TV YouTube. Run as root via stdin. Inverse: batchf-restore.sh
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchf; DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
IDS="'org.tizen.primevideo','org.tizen.primevideo-dummy','org.tizen.primevideo_companionapp','com.samsung.tv.aria-video','org.tizen.browser'"
[ "$(id -u)" = 0 ]
[ ! -e $BK ] || { echo 'batch F already applied - refusing'; exit 3; }
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
mkdir -m 700 $BK
cp -a $DB $BK/registry-before-batchf.db; sha256sum $DB $BK/registry-before-batchf.db
# exactly the rows this batch flips, for the inverse
sqlite3 -readonly $DB "select app_id from package_app_info where app_id in ($IDS) and app_disable='false';" > $BK/flipped.txt
echo "to disable: $(tr '\n' ' ' < $BK/flipped.txt)"
[ -s $BK/flipped.txt ] || { echo 'nothing to disable'; exit 0; }
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
UPDATE package_app_info SET app_disable='true' WHERE app_id IN ($IDS) AND app_disable='false';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=0 FROM package_app_info WHERE app_id IN ($IDS) AND app_disable='false';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
$HELPER read-owner $(tr '\n' ' ' < $BK/flipped.txt)
for a in $(cat $BK/flipped.txt); do app_launcher -u 5001 -k $a >/dev/null 2>&1 || true; done
sleep 2
ps -eo pid,comm,args | grep -iE 'primevideo|aria-video|browser' | grep -v grep | cut -c1-100 || echo 'none running'
grep MemAvailable /proc/meminfo
echo 'BATCH F APPLIED'
