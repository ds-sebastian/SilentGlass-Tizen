#!/bin/sh
# Inverse of update-v17b.sh: put the v17 DLL and resources back. Keeps the v17b files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v17b-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=f1061737d8f48969027a8459ae49de995cad540329763934d88c956c76107609
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v17-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v17-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v17b-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v17b ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v17b-quiet-home-removed-$ts"
mv "$BK/v17-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v17-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v17 restored (DLL $PREV). v17b resources kept at $BK/v17b-quiet-home-removed-$ts. Press Home to reopen."
