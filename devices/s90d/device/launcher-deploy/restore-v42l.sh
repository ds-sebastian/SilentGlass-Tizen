#!/bin/sh
# Inverse of update-v43a.sh: put the v42l DLL and resources back. Keeps the v43a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43a-20261027
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=c208d23dcf858593900678316e9987f17d971e7afa2f9cf44f17e375d6d7d6a9  # v42l
DLL=6589763b58fead61807aac72c718ef8bd88a41e55d83a662bc787a1b95d17a1a  # v43a: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v42l-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v42l-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$DLL" ] || { echo "installed DLL is not v43a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v43a-quiet-home-removed-$ts"
mv "$BK/v42l-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v42l-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v42l restored (DLL $PREV). v43a resources kept at $BK/v43a-quiet-home-removed-$ts. Press Home to reopen."
