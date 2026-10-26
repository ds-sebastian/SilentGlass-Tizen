#!/bin/sh
# Inverse of update-v28a.sh: put the v27a DLL and resources back. Keeps the v28a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v28a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=c9756efac20e6b5c159992b893d6a7a693d08f2892bbf8d2729b04c5eb4b0d1c
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v27a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v27a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v28a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v28a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v28a-quiet-home-removed-$ts"
mv "$BK/v27a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v27a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v27a restored (DLL $PREV). v28a resources kept at $BK/v28a-quiet-home-removed-$ts. Press Home to reopen."
