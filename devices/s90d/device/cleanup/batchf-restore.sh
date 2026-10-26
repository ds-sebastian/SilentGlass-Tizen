#!/bin/sh
# Inverse of batchf-apply.sh: re-enable exactly the apps it disabled. Run as root via stdin.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchf; DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
[ "$(id -u)" = 0 ]
[ -s $BK/flipped.txt ] || { echo 'no batch F record - nothing to restore'; exit 3; }
grep -Eqv '^[A-Za-z0-9._-]+$' $BK/flipped.txt && { echo 'unexpected content in flipped.txt - refusing'; exit 4; }
IDS=$(sed "s/.*/'&'/" $BK/flipped.txt | paste -sd, -)
N=$(wc -l < $BK/flipped.txt)
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
UPDATE package_app_info SET app_disable='false' WHERE app_id IN ($IDS) AND app_disable='true';
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=$N FROM package_app_info WHERE app_id IN ($IDS) AND app_disable='false';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
mv $BK $BK-restored-$(date +%Y%m%dT%H%M%S)
echo 'BATCH F RESTORED'
