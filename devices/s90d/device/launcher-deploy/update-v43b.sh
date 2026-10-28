#!/bin/sh
# SilentGlass v43a -> v43b. Stage v43b.tar.gz (BixbyPromotion.dll + quiet-home/) and v43b-files.sha256 (sha256sum of
# every file in it) in $ST. Refuses unless v43a is installed; keeps SMACK labels; verifies every file. Inverse: restore-v43a.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v43b-20261027
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v43b
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=d91f08ebc172058825a6a632c78698d442abed85a0536c9b744a58515190364c  # v43a
DLL=a4fdb9d652bc8244e3663592eff3f1252680a800e9ef85f2b3237564a88f1ccc  # v43b: python3 tools/build.py launcher (reproducible)
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v43b backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v43a - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(grep '  BixbyPromotion.dll$' $ST/v43b-files.sha256 | cut -d' ' -f1)" = "$DLL" ] || { echo 'staged files are not the v43b build'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v43a DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v43b.tar.gz" "$ST/v43b-files.sha256" "$BK/"
tar -xzf "$BK/v43b.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v43b-files.sha256 >/dev/null) || { echo 'staged files do not match their list - refusing'; exit 8; }
echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v43a'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v43a-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v43a-quiet-home"
[ "$(sha256sum $BK/v43a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v43b'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
# a failed check here means a broken install: restore-v43a.sh puts v43a back
(cd "$A/res" && grep '  quiet-home/' "$BK/v43b-files.sha256" | sha256sum -c - >/dev/null) || { echo 'RESOURCES DO NOT MATCH'; exit 9; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$DLL" ] || { echo 'DLL DOES NOT MATCH'; exit 9; }
echo 'resources and DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V43A INSTALLED. Press Home to open it.'
