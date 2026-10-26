#!/bin/sh
# Inverse of update-v20d.sh: put the v20c DLL and resources back. Keeps the v20d files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v20d-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=27a6d56fcb31c52b3aab00f44a1dd3dd80bcab0eb268ff9ff3bd128eba3c3eda
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v20c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v20c-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v20d-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v20d ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v20d-quiet-home-removed-$ts"
mv "$BK/v20c-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v20c-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v20c restored (DLL $PREV). v20d resources kept at $BK/v20d-quiet-home-removed-$ts. Press Home to reopen."
