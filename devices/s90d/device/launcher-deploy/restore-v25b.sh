#!/bin/sh
# Inverse of update-v26a.sh: put the v25b DLL and resources back. Keeps the v26a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v26a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=30e255655de6bc36f1b91819ae532137d0a1fb433ba22cd4190b1ada78e53a9f
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v25b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v25b-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v26a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v26a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v26a-quiet-home-removed-$ts"
mv "$BK/v25b-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v25b-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v25b restored (DLL $PREV). v26a resources kept at $BK/v26a-quiet-home-removed-$ts. Press Home to reopen."
