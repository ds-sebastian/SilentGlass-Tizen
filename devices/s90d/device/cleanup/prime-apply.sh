#!/bin/sh
# Prime Video: stop boot-time preload/prelaunch and disable its Samsung-Home companion service.
# Prime itself stays installed and launchable. Run as root via stdin. Inverse: prime-restore.sh.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024
DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
APP=org.tizen.primevideo
COMP=org.tizen.primevideo_companionapp
KEYS="'http://samsung.com/tv/metadata/appbootingsupport','http://samsung.com/tv/metadata/prelaunch.support'"
[ "$(id -u)" = 0 ]
[ ! -e "$BK/prime-restore.sql" ] || { echo 'prime change already applied (restore file exists) - refusing'; exit 3; }
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]
[ "$(sqlite3 -readonly "$DB" "select count(*) from package_app_app_metadata where app_id='$APP' and md_key in ($KEYS);")" = 2 ] || { echo 'metadata not in expected state - refusing'; exit 4; }
[ "$(sqlite3 -readonly "$DB" "select app_disable from package_app_info where app_id='$COMP';")" = false ] || { echo 'companion not in expected state - refusing'; exit 5; }

echo '=== backup'
cp -a "$DB" "$BK/registry-before-prime.db"; sha256sum "$DB" "$BK/registry-before-prime.db"
sqlite3 -readonly "$DB" ".mode insert package_app_app_metadata" "select * from package_app_app_metadata where app_id='$APP' and md_key in ($KEYS);" > "$BK/prime-restore.sql"
[ "$(grep -c '^INSERT INTO' "$BK/prime-restore.sql")" = 2 ]
cat "$BK/prime-restore.sql"

echo '=== transaction'
sqlite3 -bail "$DB" <<SQL
BEGIN IMMEDIATE;
DELETE FROM package_app_app_metadata WHERE app_id='$APP' AND md_key IN ($KEYS);
UPDATE package_app_info SET app_disable='true' WHERE app_id='$COMP' AND app_disable='false';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=0 FROM package_app_app_metadata WHERE app_id='$APP' AND md_key IN ($KEYS);
INSERT INTO g SELECT count(*)=1 FROM package_app_info WHERE app_id='$COMP' AND app_disable='true';
INSERT INTO g SELECT count(*)=1 FROM package_app_info WHERE app_id='$APP' AND app_disable='false';
COMMIT;
SQL
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]

echo '=== reload + readback'
$HELPER reload-owner
$HELPER read-owner $APP $COMP
app_launcher -u 5001 -k $APP >/dev/null 2>&1 || true
app_launcher -u 5001 -k $COMP >/dev/null 2>&1 || true
sleep 2; ps -eo pid,comm,args | grep -iE 'primevideo|aria-engine' | grep -v grep | cut -c1-120 || true
echo 'PRIME APPLY COMPLETE'
