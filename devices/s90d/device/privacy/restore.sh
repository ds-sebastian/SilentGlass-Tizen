#!/bin/sh
# Inverse of apply.sh: remove only markers carrying this phase's tag; guarded registry restore;
# owner cache reload; readback. Does not launch any app.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601
DB=/opt/dbspace/.pkgmgr_parser.db
MD=/opt/usr/share/systemd/system
TAG=s90d-privacy-phaseB-20261024T135601
MARKERS="com.samsung.tv.bixby-prov com.samsung.tv.farfieldvoice com.samsung.tv.ondevice-voice com.samsung.tv.voiceid-service com.samsung.tv.wakeupless org.tizen.voice-app org.tizen.voice-client"
HELPER="/usr/bin/dotnet $BK/AppMetadata.dll"

[ "$(id -u)" = 0 ]
cd "$BK"
sha256sum -c files.sha256
APPS=$(cat apps.txt)
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]

echo '=== REGISTRY RESTORE (guarded: all 19 must currently be true)'
sqlite3 -bail "$DB" < "$BK/restore.sql"
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]

echo '=== MARKERS'
for m in $MARKERS; do
  if [ ! -e "$MD/$m" ]; then echo "$m absent"; continue; fi
  if [ "$(cat "$MD/$m")" = "$TAG" ]; then rm -f "$MD/$m"; echo "$m removed"; else echo "$m has foreign content - left in place"; fi
done

echo '=== OWNER CACHE RELOAD + READBACK'
$HELPER reload-owner
$HELPER read-owner $APPS > "$BK/native-restored.jsonl"
echo "owner disabled=false: $(grep -c '"disabled":false' "$BK/native-restored.jsonl")/19"
echo 'RESTORE COMPLETE. Apps/services return on their normal triggers or next reboot.'
