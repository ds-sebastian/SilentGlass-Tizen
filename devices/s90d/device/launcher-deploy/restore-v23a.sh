#!/bin/sh
# Inverse of update-v24a.sh: put the v23a DLL and resources back. Keeps the v24a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v24a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=655ececdffbcbcb58567df0690400b5801559e63b8a399c1902e0e943c3e3e91
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v23a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v23a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v24a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v24a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v24a-quiet-home-removed-$ts"
mv "$BK/v23a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v23a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v23a restored (DLL $PREV). v24a resources kept at $BK/v24a-quiet-home-removed-$ts. Press Home to reopen."
