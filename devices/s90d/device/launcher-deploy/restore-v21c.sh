#!/bin/sh
# Inverse of update-v21d.sh: put the v21c DLL and resources back. Keeps the v21d files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v21d-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=046fa661be9ec6824e2ad764a80a249107c0ccc88cef426f33a699b6e0e88201
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v21c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v21c-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v21d-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v21d ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v21d-quiet-home-removed-$ts"
mv "$BK/v21c-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v21c-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v21c restored (DLL $PREV). v21d resources kept at $BK/v21d-quiet-home-removed-$ts. Press Home to reopen."
