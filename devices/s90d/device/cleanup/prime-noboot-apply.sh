set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024; DB=/opt/dbspace/.pkgmgr_parser.db; APP=org.tizen.primevideo
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
[ "$(id -u)" = 0 ]
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
[ "$(sqlite3 -readonly $DB "select count(*) from package_app_app_metadata where app_id='$APP' and md_key='http://samsung.com/tv/metadata/disable.appbooting';")" = 0 ] || { echo 'already set - refusing'; exit 3; }
echo "reference rows:"; sqlite3 -readonly $DB "select * from package_app_app_metadata where md_key='http://samsung.com/tv/metadata/disable.appbooting';"
cp -a $DB $BK/registry-before-prime-noboot.db; sha256sum $DB $BK/registry-before-prime-noboot.db
sqlite3 -bail $DB <<SQL
BEGIN IMMEDIATE;
INSERT INTO package_app_app_metadata(app_id,md_key,md_value) VALUES('$APP','http://samsung.com/tv/metadata/disable.appbooting','true');
CREATE TEMP TABLE g(ok INTEGER CHECK(ok=1));
INSERT INTO g SELECT count(*)=1 FROM package_app_app_metadata WHERE app_id='$APP' AND md_key='http://samsung.com/tv/metadata/disable.appbooting' AND md_value='true';
COMMIT;
SQL
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER reload-owner
app_launcher -u 5001 -k $APP 2>&1 | tail -n 1; sleep 2
ps -eo pid,comm | grep -x ' *[0-9]* primevideo' || echo 'prime not running'
echo 'PRIME NO-BOOT APPLIED'
