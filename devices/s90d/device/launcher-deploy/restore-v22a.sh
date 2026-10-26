#!/bin/sh
# Inverse of update-v23a.sh: put the v22a DLL and resources back. Keeps the v23a files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v23a-20261025
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=29de896d1b3a4fd1d6339216823ca695fa7a0a091a3af7d613d16d9255ed177b
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v22a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v22a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$(grep '  BixbyPromotion.dll$' $BK/v23a-files.sha256 | cut -d' ' -f1)" ] || { echo "installed DLL is not v23a ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v23a-quiet-home-removed-$ts"
mv "$BK/v22a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v22a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v22a restored (DLL $PREV). v23a resources kept at $BK/v23a-quiet-home-removed-$ts. Press Home to reopen."
