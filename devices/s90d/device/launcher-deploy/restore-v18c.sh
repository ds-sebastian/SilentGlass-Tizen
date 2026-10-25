#!/bin/sh
# Inverse of update-v18d.sh: put the v18c DLL and resources back. Keeps the v18d files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v18d-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=c33c8b508b70956748f133992ff487db78a87121f1b3af9e007e3edc36757199
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v18c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v18c-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v18d-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v18d ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v18d-quiet-home-removed-$ts"
mv "$BK/v18c-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v18c-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v18c restored (DLL $PREV). v18d resources kept at $BK/v18d-quiet-home-removed-$ts. Press Home to reopen."
