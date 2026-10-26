#!/bin/sh
# Inverse of update-v25b.sh: put the v25a DLL and resources back. Keeps the v25b files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v25b-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=2c1ad646e7187d6466ff79943b780271cb3f300eaefa9af6547b9da2ec231109
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v25a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v25a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v25b-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v25b ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v25b-quiet-home-removed-$ts"
mv "$BK/v25a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v25a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v25a restored (DLL $PREV). v25b resources kept at $BK/v25b-quiet-home-removed-$ts. Press Home to reopen."
