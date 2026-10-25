#!/bin/sh
# Inverse of update-v15.sh: put the v14 DLL and resources back. Keeps the v15 files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v15-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=eb45a146f248a9324a97537c5692a94f62bf6b49e3a787ec81e111ac33941c9d
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v14-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v14-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v15-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v15 ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v15-quiet-home-removed-$ts"
mv "$BK/v14-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v14-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v14 restored (DLL $PREV). v15 resources kept at $BK/v15-quiet-home-removed-$ts. Press Home to reopen."
