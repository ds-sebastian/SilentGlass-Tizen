#!/bin/sh
# Batch C (2026-10-25): disable ad targeting + voice leftovers via registry app_disable (same lever as Phase B). Inverse: batchc-restore.sh
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024; DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
[ "$(id -u)" = 0 ]
[ ! -e $BK/batchc-restore.list ] || { echo 'batch C already applied - refusing'; exit 3; }
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
[ "$(sqlite3 -readonly $DB "select count(*) from package_app_info where app_id in ('com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard') and app_disable='false';")" = 4 ] || { echo 'not all rows in expected state - refusing'; sqlite3 -readonly $DB "select app_id,app_disable from package_app_info where app_id in ('com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard');"; exit 4; }
cp -a $DB $BK/registry-before-batchc.db; sha256sum $DB $BK/registry-before-batchc.db
sqlite3 -readonly $DB "select app_id from package_app_info where app_id in ('com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard') and app_disable='false';" > $BK/batchc-restore.list
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
UPDATE package_app_info SET app_disable='true' WHERE app_id IN ('com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard') AND app_disable='false';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=4 FROM package_app_info WHERE app_id IN ('com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard') AND app_disable='true';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
$HELPER read-owner com.samsung.tv.targeted-ad-service com.samsung.tv.utterance-recommender-core com.samsung.tv.AICompanionAgent com.samsung.tv.aov-dashboard
for a in com.samsung.tv.targeted-ad-service com.samsung.tv.utterance-recommender-core com.samsung.tv.AICompanionAgent com.samsung.tv.aov-dashboard; do app_launcher -u 5001 -k $a >/dev/null 2>&1 || true; done
sleep 3; app_launcher -S 2>/dev/null | grep -E 'targeted-ad|utterance|AICompanion|aov-dashboard' || echo 'none of the batch C apps running'
grep MemAvailable /proc/meminfo
echo 'BATCH C APPLIED'
