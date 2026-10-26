#!/bin/sh
# Inverse of update-v21b.sh: put the v21a DLL and resources back. Keeps the v21b files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v21b-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=63c975c59cea8a24a698cfa075eaf548ed0b69207c4e1fd154d7c0d33fd4e9fd
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v21a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v21a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v21b-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v21b ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v21b-quiet-home-removed-$ts"
mv "$BK/v21a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v21a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v21a restored (DLL $PREV). v21b resources kept at $BK/v21b-quiet-home-removed-$ts. Press Home to reopen."
