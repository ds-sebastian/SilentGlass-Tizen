#!/bin/sh
# Inverse of prime-apply.sh: re-insert the two saved metadata rows and re-enable the companion service.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024
DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
APP=org.tizen.primevideo
COMP=org.tizen.primevideo_companionapp
KEYS="'http://samsung.com/tv/metadata/appbootingsupport','http://samsung.com/tv/metadata/prelaunch.support'"
[ "$(id -u)" = 0 ]
[ "$(grep -c '^INSERT INTO' "$BK/prime-restore.sql")" = 2 ]
# Samsung reinstalls Prime Video now and then, which brings its boot metadata back: then only the companion is left
have=$(sqlite3 -readonly "$DB" "select count(*) from package_app_app_metadata where app_id='$APP' and md_key in ($KEYS);")
case "$have" in 0) ins="$BK/prime-restore.sql";; 2) ins=/dev/null; echo 'boot metadata already back (Samsung reinstalled Prime) - skipped';; *) echo "partial metadata ($have) - refusing"; exit 4;; esac
{ echo 'BEGIN IMMEDIATE;'; cat "$ins"; echo "UPDATE package_app_info SET app_disable='false' WHERE app_id='$COMP' AND app_disable='true';"; echo 'COMMIT;'; } | sqlite3 -bail "$DB"
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
$HELPER read-owner $APP $COMP
mv "$BK/prime-restore.sql" "$BK/prime-restore.sql.applied-$(date +%Y%m%dT%H%M%S)"
echo 'PRIME RESTORED'
