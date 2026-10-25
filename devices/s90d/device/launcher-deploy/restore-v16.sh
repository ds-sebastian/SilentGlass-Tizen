#!/bin/sh
# Inverse of update-v16b.sh: put the v16 DLL and resources back. Keeps the v16b files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v16b-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=faa212650de9cf5cdba4559f2e26e615c216607751b677c95cccb7fec2bc777f
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v16-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v16-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v16b-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v16b ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v16b-quiet-home-removed-$ts"
mv "$BK/v16-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v16-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v16 restored (DLL $PREV). v16b resources kept at $BK/v16b-quiet-home-removed-$ts. Press Home to reopen."
