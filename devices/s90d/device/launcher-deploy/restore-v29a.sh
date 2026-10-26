#!/bin/sh
# Inverse of update-v29b.sh: put the v29a DLL and resources back. Keeps the v29b files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v29b-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=64b4007e554bcc9d2df57976e33c111ab9453d246812d316d9a7f2e6be3d2790
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v29a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v29a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v29b-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v29b ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v29b-quiet-home-removed-$ts"
mv "$BK/v29a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v29a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v29a restored (DLL $PREV). v29b resources kept at $BK/v29b-quiet-home-removed-$ts. Press Home to reopen."
