#!/bin/sh
# Batch E (2026-10-26): content-info feed for ACR, usage analytics, voice-assistant leftovers and the ad players, via
# registry app_disable (same lever as Phase B / batch C). Picked from Vetro's tracking/voice catalog and checked on this
# TV. Deliberately NOT included: coba.adagent (Samsung Home loads it), content-grab-service and nvision-analysis-dbmgr
# (may feed picture processing / Ambient), anything SmartThings/IoT/Matter (the Thread border router needs them).
# Inverse: batche-restore.sh
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024; DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
[ "$(id -u)" = 0 ]
[ ! -e $BK/batche-restore.list ] || { echo 'batch E already applied - refusing'; exit 3; }
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
[ "$(sqlite3 -readonly $DB "select count(*) from package_app_info where app_id in ('com.samsung.tv.cis-api-service-app','com.samsung.tv.cis','com.samsung.tizen.samsung-analytics','org.tizen.multi-assistant-service','com.samsung.tv.aov-service','com.samsung.tv.voice-instant-app','org.tizen.voice-onboarding','com.samsung.tv.mybixby','com.samsung.tv.bixby-context-client','com.samsung.tv.bixbycapsuleviewer','adplatform.ADPlayer','com.samsung.tv.adplayer','com.samsung.tv.ad-web-framework');")" = 13 ] || { echo 'not all apps present - refusing'; exit 4; }
cp -a $DB $BK/registry-before-batche.db; sha256sum $DB $BK/registry-before-batche.db
sqlite3 -readonly $DB "select app_id from package_app_info where app_id in ('com.samsung.tv.cis-api-service-app','com.samsung.tv.cis','com.samsung.tizen.samsung-analytics','org.tizen.multi-assistant-service','com.samsung.tv.aov-service','com.samsung.tv.voice-instant-app','org.tizen.voice-onboarding','com.samsung.tv.mybixby','com.samsung.tv.bixby-context-client','com.samsung.tv.bixbycapsuleviewer','adplatform.ADPlayer','com.samsung.tv.adplayer','com.samsung.tv.ad-web-framework') and app_disable='false';" > $BK/batche-restore.list
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
UPDATE package_app_info SET app_disable='true' WHERE app_id IN ('com.samsung.tv.cis-api-service-app','com.samsung.tv.cis','com.samsung.tizen.samsung-analytics','org.tizen.multi-assistant-service','com.samsung.tv.aov-service','com.samsung.tv.voice-instant-app','org.tizen.voice-onboarding','com.samsung.tv.mybixby','com.samsung.tv.bixby-context-client','com.samsung.tv.bixbycapsuleviewer','adplatform.ADPlayer','com.samsung.tv.adplayer','com.samsung.tv.ad-web-framework') AND app_disable='false';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=13 FROM package_app_info WHERE app_id IN ('com.samsung.tv.cis-api-service-app','com.samsung.tv.cis','com.samsung.tizen.samsung-analytics','org.tizen.multi-assistant-service','com.samsung.tv.aov-service','com.samsung.tv.voice-instant-app','org.tizen.voice-onboarding','com.samsung.tv.mybixby','com.samsung.tv.bixby-context-client','com.samsung.tv.bixbycapsuleviewer','adplatform.ADPlayer','com.samsung.tv.adplayer','com.samsung.tv.ad-web-framework') AND app_disable='true';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
$HELPER read-owner com.samsung.tv.cis-api-service-app com.samsung.tv.cis com.samsung.tizen.samsung-analytics org.tizen.multi-assistant-service com.samsung.tv.aov-service com.samsung.tv.voice-instant-app org.tizen.voice-onboarding com.samsung.tv.mybixby com.samsung.tv.bixby-context-client com.samsung.tv.bixbycapsuleviewer adplatform.ADPlayer com.samsung.tv.adplayer com.samsung.tv.ad-web-framework
for a in com.samsung.tv.cis-api-service-app com.samsung.tv.cis com.samsung.tizen.samsung-analytics org.tizen.multi-assistant-service com.samsung.tv.aov-service com.samsung.tv.voice-instant-app org.tizen.voice-onboarding com.samsung.tv.mybixby com.samsung.tv.bixby-context-client com.samsung.tv.bixbycapsuleviewer adplatform.ADPlayer com.samsung.tv.adplayer com.samsung.tv.ad-web-framework; do app_launcher -u 5001 -k $a >/dev/null 2>&1 || true; done
sleep 3; app_launcher -S 2>/dev/null | grep -E 'cis-|samsung-analytics|multi-assistant|aov-service|voice-instant|voice-onboarding|mybixby|bixby-context|bixbycapsule|adplayer|ADPlayer|ad-web' || echo 'none of the batch E apps running'
grep MemAvailable /proc/meminfo
echo 'BATCH E APPLIED'
