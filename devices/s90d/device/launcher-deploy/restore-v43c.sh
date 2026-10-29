#!/bin/sh
# Inverse of update-v43e.sh: put the v43c DLL and resources back. Keeps the v43e files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43e-20261029
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=3f559e16d33ee896ff9564a807737a9cf558ac45e175ffcd391d37ad1149ee86  # v43c
DLL=d6544243e9cf0e61ca10108a45cec71fb4219774063beac40fab7c414c1bd7d6  # v43e: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v43c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v43c-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$DLL" ] || { echo "installed DLL is not v43e ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v43e-quiet-home-removed-$ts"
mv "$BK/v43c-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v43c-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v43c restored (DLL $PREV). v43e resources kept at $BK/v43e-quiet-home-removed-$ts. Press Home to reopen."
