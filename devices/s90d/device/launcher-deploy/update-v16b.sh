#!/bin/sh
# Quiet Home v16 -> v16b (live preview: soft fade under the name). Inverse: restore-v16.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v16b-20261025
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v16b
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=faa212650de9cf5cdba4559f2e26e615c216607751b677c95cccb7fec2bc777f
TARSHA=e6ed7f19e33c0730853591fd4a1c5aa1de620e2c4737a4997e9f0339efe9acd1
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v16b backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v16 - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(sha256sum $ST/v16b.tar.gz | cut -d' ' -f1)" = "$TARSHA" ] || { echo 'staged archive hash mismatch'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v16 DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v16b.tar.gz" "$ST/v16b-files.sha256" "$BK/"
tar -xzf "$BK/v16b.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v16b-files.sha256 >/dev/null) && echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v16'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v16-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v16-quiet-home"
[ "$(sha256sum $BK/v16-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v16b'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
(cd "$A/res" && sed -n 's#  quiet-home/#  quiet-home/#p' "$BK/v16b-files.sha256" | sha256sum -c - >/dev/null) && echo 'resources verified'
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$(grep '  BixbyPromotion.dll$' $BK/v16b-files.sha256 | cut -d' ' -f1)" ] && echo 'DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V16B INSTALLED. Press Home to open the new Quiet Home.'
