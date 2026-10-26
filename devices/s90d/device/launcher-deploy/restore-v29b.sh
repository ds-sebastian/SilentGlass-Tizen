#!/bin/sh
# Inverse of update-v31c.sh: put the v29b DLL and resources back. Keeps the v31c files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v31c-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=199564e9b93365d86484f2fc32f286b58ce46b324d379805f0eb0635bd753317
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v29b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v29b-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v31c-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v31c ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v31c-quiet-home-removed-$ts"
mv "$BK/v29b-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v29b-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v29b restored (DLL $PREV). v31c resources kept at $BK/v31c-quiet-home-removed-$ts. Press Home to reopen."
