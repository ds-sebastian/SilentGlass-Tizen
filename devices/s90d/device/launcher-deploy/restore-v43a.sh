#!/bin/sh
# Inverse of update-v43c.sh: put the v43a DLL and resources back. Keeps the v43c files aside, deletes nothing.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43c-20261028
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=d91f08ebc172058825a6a632c78698d442abed85a0536c9b744a58515190364c  # v43a
DLL=3f559e16d33ee896ff9564a807737a9cf558ac45e175ffcd391d37ad1149ee86  # v43c: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ "$(sha256sum $BK/v43a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
[ -d "$BK/v43a-quiet-home" ]
cur=$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)
[ "$cur" = "$DLL" ] || { echo "installed DLL is not v43c ($cur) - refusing"; exit 4; }
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$BK/v43c-quiet-home-removed-$ts"
mv "$BK/v43a-quiet-home" "$A/res/quiet-home"
cp -a "$BK/v43a-BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]
echo "v43a restored (DLL $PREV). v43c resources kept at $BK/v43c-quiet-home-removed-$ts. Press Home to reopen."
