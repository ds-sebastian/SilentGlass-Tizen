#!/bin/sh
# Inverse of update-v12.sh: put the v11 DLL and resources back. Keeps the v12 files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v12-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=97598c8a15ee1ffd7c943ac705110db1255e2c11b56c1bb4db300a82b3bcd64f
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v11-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v11-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v12-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v12 ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v12-quiet-home-removed-$ts"
mv "$BK/v11-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v11-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v11 restored (DLL $PREV). v12 resources kept at $BK/v12-quiet-home-removed-$ts. Press Home to reopen."
