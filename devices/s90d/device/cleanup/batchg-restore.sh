#!/bin/sh
# Inverse of batchg-apply.sh: put back the update settings saved before batch G. Run as root via stdin.
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/batchg
[ "$(id -u)" = 0 ]
[ -s $BK/before.txt ] || { echo 'no batch G backup - nothing to restore'; exit 3; }
while read -r key value; do
  case "$key" in
    db/built-in-app-upgrader/server_upgrade) [ "$value" = True ] && v=1 || v=0; vconftool set -f -t bool "$key" $v ;;
    db/SyncML/ivi-oma-dm-service/software-update/autoupdate/setting) case "$value" in 0|1) vconftool set -f -t int "$key" "$value" ;; *) exit 4;; esac ;;
    *) echo "unexpected key $key"; exit 4 ;;
  esac
  vconftool get "$key"
done < $BK/before.txt
mv $BK $BK.restored-$(date +%Y%m%d%H%M%S)
echo 'BATCH G RESTORED'
