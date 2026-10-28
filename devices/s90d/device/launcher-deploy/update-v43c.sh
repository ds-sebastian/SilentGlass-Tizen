#!/bin/sh
# SilentGlass v43b -> v43c. Stage v43c.tar.gz (BixbyPromotion.dll + quiet-home/) and v43c-files.sha256 (sha256sum of
# every file in it) in $ST. Refuses unless v43b is installed; keeps SMACK labels; verifies every file. Inverse: restore-v43b.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43c-20261028
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v43c
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=a4fdb9d652bc8244e3663592eff3f1252680a800e9ef85f2b3237564a88f1ccc  # v43b
DLL=3f559e16d33ee896ff9564a807737a9cf558ac45e175ffcd391d37ad1149ee86  # v43c: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v43c backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v43b - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(grep '  BixbyPromotion.dll$' $ST/v43c-files.sha256 | cut -d' ' -f1)" = "$DLL" ] || { echo 'staged files are not the v43c build'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v43b DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v43c.tar.gz" "$ST/v43c-files.sha256" "$BK/"
tar -xzf "$BK/v43c.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v43c-files.sha256 >/dev/null) || { echo 'staged files do not match their list - refusing'; exit 8; }
echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v43b'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v43b-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v43b-quiet-home"
[ "$(sha256sum $BK/v43b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v43c'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
# a failed check here means a broken install: restore-v43b.sh puts v43b back
(cd "$A/res" && grep '  quiet-home/' "$BK/v43c-files.sha256" | sha256sum -c - >/dev/null) || { echo 'RESOURCES DO NOT MATCH'; exit 9; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$DLL" ] || { echo 'DLL DOES NOT MATCH'; exit 9; }
echo 'resources and DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V43A INSTALLED. Press Home to open it.'
