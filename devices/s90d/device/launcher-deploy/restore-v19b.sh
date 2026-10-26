#!/bin/sh
# Inverse of update-v19c.sh: put the v19b DLL and resources back. Keeps the v19c files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v19c-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=8a4db5538bc3ed108e130a6dc38daf6bb92e7bc7af3ff9c109a0d7ecbd8463a2
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v19b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v19b-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v19c-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v19c ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v19c-quiet-home-removed-$ts"
mv "$BK/v19b-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v19b-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v19b restored (DLL $PREV). v19c resources kept at $BK/v19c-quiet-home-removed-$ts. Press Home to reopen."
