set -eu
DB=/opt/dbspace/.pkgmgr_parser.db; APP=org.tizen.primevideo
[ "$(id -u)" = 0 ]
sqlite3 -bail $DB "BEGIN IMMEDIATE; DELETE FROM package_app_app_metadata WHERE app_id='$APP' AND md_key='http://samsung.com/tv/metadata/disable.appbooting' AND md_value='true'; COMMIT;"
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll reload-owner
echo 'PRIME NO-BOOT RESTORED'
