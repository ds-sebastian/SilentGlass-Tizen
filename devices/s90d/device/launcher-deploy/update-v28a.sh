#!/bin/sh
# Quiet Home v27a -> v28a (input requests from the HA bridge). Inverse: restore-v27a.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v28a-20261025
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v28a
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=c9756efac20e6b5c159992b893d6a7a693d08f2892bbf8d2729b04c5eb4b0d1c
TARSHA=e16b10ec08a0696343b9826b2dce45710cdf3ff9ba4bf49fba9a5c74d451d64d
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v28a backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v27a - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(sha256sum $ST/v28a.tar.gz | cut -d' ' -f1)" = "$TARSHA" ] || { echo 'staged archive hash mismatch'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v27a DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v28a.tar.gz" "$ST/v28a-files.sha256" "$BK/"
tar -xzf "$BK/v28a.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v28a-files.sha256 >/dev/null) && echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v27a'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v27a-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v27a-quiet-home"
[ "$(sha256sum $BK/v27a-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v28a'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
(cd "$A/res" && sed -n 's#  quiet-home/#  quiet-home/#p' "$BK/v28a-files.sha256" | sha256sum -c - >/dev/null) && echo 'resources verified'
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$(grep '  BixbyPromotion.dll$' $BK/v28a-files.sha256 | cut -d' ' -f1)" ] && echo 'DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V28A INSTALLED. Press Home to open the new Quiet Home.'
