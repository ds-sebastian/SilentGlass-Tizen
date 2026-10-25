#!/bin/sh
# Inverse of update-v14.sh: put the v13 DLL and resources back. Keeps the v14 files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v14-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=61d543c16817d1229190efc51c049297bb6cee5fd62d6d682555c675a8e306ad
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v13-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v13-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v14-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v14 ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v14-quiet-home-removed-$ts"
mv "$BK/v13-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v13-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v13 restored (DLL $PREV). v14 resources kept at $BK/v14-quiet-home-removed-$ts. Press Home to reopen."
