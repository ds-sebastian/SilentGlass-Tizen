#!/bin/sh
# Inverse of update-v22a.sh: put the v21d DLL and resources back. Keeps the v22a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v22a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=95a0a62f39a733a08efe90b7ccdbf010b440bfb0646fa9a20608100617b79c3e
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v21d-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v21d-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v22a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v22a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v22a-quiet-home-removed-$ts"
mv "$BK/v21d-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v21d-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v21d restored (DLL $PREV). v22a resources kept at $BK/v22a-quiet-home-removed-$ts. Press Home to reopen."
