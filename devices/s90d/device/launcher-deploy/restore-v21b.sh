#!/bin/sh
# Inverse of update-v21c.sh: put the v21b DLL and resources back. Keeps the v21c files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v21c-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=2573819a25f751df82806d6ab699cce24184f06151b85e6c381397acbab93344
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v21b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v21b-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v21c-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v21c ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v21c-quiet-home-removed-$ts"
mv "$BK/v21b-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v21b-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v21b restored (DLL $PREV). v21c resources kept at $BK/v21c-quiet-home-removed-$ts. Press Home to reopen."
