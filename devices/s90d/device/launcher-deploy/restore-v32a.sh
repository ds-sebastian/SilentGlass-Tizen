#!/bin/sh
# Inverse of update-v42l.sh: put the v32a DLL and resources back. Keeps the v42l files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v42l-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=4edf70c0a22d3ccf08b1adc1302a14a9779f1484053d455939a3ca7a11710529
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v32a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v32a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v42l-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v42l ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v42l-quiet-home-removed-$ts"
mv "$BK/v32a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v32a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v32a restored (DLL $PREV). v42l resources kept at $BK/v42l-quiet-home-removed-$ts. Press Home to reopen."
