#!/bin/sh
# Inverse of update-v13.sh: put the v12 DLL and resources back. Keeps the v13 files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v13-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=3e031d4bdf26d3a8e2cbdd61a6fb8e0fbe40722ee0c2dee04eec69c7858892d6
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v12-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v12-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v13-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v13 ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v13-quiet-home-removed-$ts"
mv "$BK/v12-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v12-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v12 restored (DLL $PREV). v13 resources kept at $BK/v13-quiet-home-removed-$ts. Press Home to reopen."
