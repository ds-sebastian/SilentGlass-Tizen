#!/bin/sh
# Batch G (2026-10-26): stop Samsung's automatic updates that ignore the menu settings.
# - db/built-in-app-upgrader/server_upgrade: read by org.tizen.was-builtinupgrade, which updates Samsung's built-in
#   apps whatever "Auto update apps" says (that menu setting is db/apps/auto_update, read by was-appupdate). It
#   reinstalled com.samsung.tv.aov-dashboard enabled on 2026-10-26 with the menu setting off.
# - db/SyncML/ivi-oma-dm-service/software-update/autoupdate/setting: the OMA-DM firmware auto-update, separate from
#   the menu's Software Update > Auto update (db/menu/support/softwareupdate/autoupdate). A firmware update can
#   close the access this mod is maintained with.
# Manual updates (Settings > Support > Software Update, the app store) still work.
# Run as root via stdin. Inverse: batchg-restore.sh (puts back the saved values).
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchg
[ "$(id -u)" = 0 ]
[ ! -e $BK ] || { echo 'batch G already applied - refusing'; exit 3; }
get() { vconftool get "$1" 2>/dev/null | sed -n 's/.*value = \([^ ]*\) (\(.*\))$/\1 \2/p'; }
A=db/built-in-app-upgrader/server_upgrade
F=db/SyncML/ivi-oma-dm-service/software-update/autoupdate/setting
a=$(get $A); f=$(get $F)
case "$a" in "True Boolean"|"False Boolean") ;; *) echo "unexpected $A: $a"; exit 4;; esac
case "$f" in "0 Int32"|"1 Int32") ;; *) echo "unexpected $F: $f"; exit 4;; esac
mkdir -m 700 $BK
printf '%s %s\n%s %s\n' $A "${a% *}" $F "${f% *}" > $BK/before.txt
cat $BK/before.txt
vconftool set -f -t bool $A 0
vconftool set -f -t int $F 0
[ "$(get $A)" = "False Boolean" ] && [ "$(get $F)" = "0 Int32" ]
echo "now: $A=$(get $A) $F=$(get $F)"
echo 'BATCH G APPLIED'
