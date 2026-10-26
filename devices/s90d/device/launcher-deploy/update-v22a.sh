#!/bin/sh
# Quiet Home v21d -> v22a (refactor: drop dead paths, HomeLogic, prefs load fix). Inverse: restore-v21d.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v22a-20261025
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v22a
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=95a0a62f39a733a08efe90b7ccdbf010b440bfb0646fa9a20608100617b79c3e
TARSHA=4d57e95e78073c9d1e128030bfa0778fe1bd1351972d16ab1318b4ebf6497c6a
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v22a backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v21d - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(sha256sum $ST/v22a.tar.gz | cut -d' ' -f1)" = "$TARSHA" ] || { echo 'staged archive hash mismatch'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v21d DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v22a.tar.gz" "$ST/v22a-files.sha256" "$BK/"
tar -xzf "$BK/v22a.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v22a-files.sha256 >/dev/null) && echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v21d'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v21d-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v21d-quiet-home"
[ "$(sha256sum $BK/v21d-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v22a'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
(cd "$A/res" && sed -n 's#  quiet-home/#  quiet-home/#p' "$BK/v22a-files.sha256" | sha256sum -c - >/dev/null) && echo 'resources verified'
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$(grep '  BixbyPromotion.dll$' $BK/v22a-files.sha256 | cut -d' ' -f1)" ] && echo 'DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V22A INSTALLED. Press Home to open the new Quiet Home.'
