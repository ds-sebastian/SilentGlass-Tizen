#!/bin/sh
# Inverse of update-v27a.sh: put the v26a DLL and resources back. Keeps the v27a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v27a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=37645344b9adfe6160b260972cbbe7a6df7bd68d4b353710b024018c9106b992
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v26a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v26a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v27a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v27a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v27a-quiet-home-removed-$ts"
mv "$BK/v26a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v26a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v26a restored (DLL $PREV). v27a resources kept at $BK/v27a-quiet-home-removed-$ts. Press Home to reopen."
