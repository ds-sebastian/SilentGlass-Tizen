#!/bin/sh
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024; DB=/opt/dbspace/.pkgmgr_parser.db
[ "$(id -u)" = 0 ]; [ -s $BK/batche-restore.list ]
L=$(sed "s/.*/'&'/" $BK/batche-restore.list | paste -sd, -)
sqlite3 -bail $DB "BEGIN IMMEDIATE; UPDATE package_app_info SET app_disable='false' WHERE app_id IN ($L) AND app_disable='true'; COMMIT;"
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll reload-owner
mv $BK/batche-restore.list $BK/batche-restore.list.applied-$(date +%Y%m%dT%H%M%S)
echo 'BATCH E RESTORED (services start again at next boot)'
