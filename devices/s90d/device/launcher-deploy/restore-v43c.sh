#!/bin/sh
# Inverse of update-v43d.sh: put the v43c DLL and resources back. Keeps the v43d files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43d-20261029
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=3f559e16d33ee896ff9564a807737a9cf558ac45e175ffcd391d37ad1149ee86  # v43c
DLL=688902e78f59408b0bfe34d259402a6f4f141cf7a1af01853d83da5c0c80b669  # v43d: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v43c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v43c-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$DLL" ] || { echo "installed DLL is not v43d ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v43d-quiet-home-removed-$ts"
mv "$BK/v43c-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v43c-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v43c restored (DLL $PREV). v43d resources kept at $BK/v43d-quiet-home-removed-$ts. Press Home to reopen."
