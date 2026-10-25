#!/bin/sh
# Phase B persistent privacy batch: 7 vendor voice stop markers + 19-app registry app_disable.
# Run as root via stdin. Inverse: restore.sh in the same directory.
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
[ "$(echo "$APPS" | wc -l)" = 19 ]
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]

echo '=== PRECHECK (no mutation yet)'
for m in $MARKERS; do
  [ ! -e "$MD/$m" ] || { echo "marker $m already exists - refusing"; exit 4; }
  tr -d '\r' < "/usr/lib/systemd/system/$m.service" | grep -Fxq "ConditionPathExists=!$MD/$m" \
    || { echo "unit $m lacks exact stop condition - refusing"; exit 5; }
done
$HELPER read-owner $APPS > "$BK/native-before.jsonl"
[ "$(grep -c '"disabled":false' "$BK/native-before.jsonl")" = 19 ] || { cat "$BK/native-before.jsonl"; echo 'native baseline mismatch'; exit 6; }
echo 'precheck ok: markers absent, conditions exact, 19 apps natively enabled'

echo '=== MARKERS'
created=""
cleanup_markers() {
  for m in $created; do
    [ "$(cat "$MD/$m")" = "$TAG" ] && rm -f "$MD/$m" && echo "removed own marker $m"
  done
}
for m in $MARKERS; do
  printf '%s\n' "$TAG" > "$MD/$m"
  created="$created $m"
  chmod 600 "$MD/$m"
  chsmack -a User "$MD/$m"
done
for m in $MARKERS; do echo "$m: $(cat "$MD/$m") $(stat -c '%U:%G %a' "$MD/$m") $(chsmack "$MD/$m" | sed 's/.* access=//')"; done

echo '=== REGISTRY TRANSACTION'
if ! sqlite3 -bail "$DB" < "$BK/apply.sql"; then
  echo 'registry transaction refused/failed - rolling back markers'
  cleanup_markers
  exit 7
fi
[ "$(sqlite3 -readonly "$DB" 'PRAGMA quick_check;')" = ok ]
L=""; for a in $APPS; do L="$L'$a',"; done; L=${L%,}
echo "rows now disabled: $(sqlite3 -readonly "$DB" "select count(*) from package_app_info where app_disable='true' and app_id in ($L);")"

echo '=== OWNER CACHE RELOAD + READBACK'
$HELPER reload-owner
$HELPER read-owner $APPS > "$BK/native-after-owner.jsonl"
$HELPER read-root $APPS > "$BK/native-after-root.jsonl"
echo "owner disabled=true: $(grep -c '"disabled":true' "$BK/native-after-owner.jsonl")/19"
echo "root  disabled=true: $(grep -c '"disabled":true' "$BK/native-after-root.jsonl")/19"
echo 'APPLY COMPLETE. Running instances keep running until terminated or reboot.'
