#!/bin/sh
# Inverse of reapply-apply.sh: re-enable exactly the apps its latest run disabled. Run as root via stdin.
set -eu
DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
[ "$(id -u)" = 0 ]
BK=$(ls -d /opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/reapply-* 2>/dev/null | grep -v -- -restored | sort | tail -n 1)
[ -n "$BK" ] && [ -s $BK/flipped.txt ] || { echo 'no re-apply record - nothing to restore'; exit 3; }
grep -Eqv '^[A-Za-z0-9._-]+$' $BK/flipped.txt && { echo 'unexpected content in flipped.txt - refusing'; exit 4; }
for a in $(cat $BK/flipped.txt); do pkgcmd -A -t app -n $a --global 2>&1 | grep -E 'key\[end\]' || true; done
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER read-owner $(tr '\n' ' ' < $BK/flipped.txt)
mv $BK $BK-restored
echo "RESTORED: $BK"
