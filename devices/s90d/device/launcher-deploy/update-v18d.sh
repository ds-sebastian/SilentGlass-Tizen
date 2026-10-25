#!/bin/sh
# Quiet Home v18c -> v18d (Apple look defaults: glass+squircle, shadows off). Inverse: restore-v18c.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v18d-20261025
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v18d
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=c33c8b508b70956748f133992ff487db78a87121f1b3af9e007e3edc36757199
TARSHA=c3beeccba1d1d3c99bad9d4b783c96b91004b78f7a092ab2e07ab1789ab7719e
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v18d backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v18c - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(sha256sum $ST/v18d.tar.gz | cut -d' ' -f1)" = "$TARSHA" ] || { echo 'staged archive hash mismatch'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v18c DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v18d.tar.gz" "$ST/v18d-files.sha256" "$BK/"
tar -xzf "$BK/v18d.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v18d-files.sha256 >/dev/null) && echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v18c'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v18c-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v18c-quiet-home"
[ "$(sha256sum $BK/v18c-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v18d'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
(cd "$A/res" && sed -n 's#  quiet-home/#  quiet-home/#p' "$BK/v18d-files.sha256" | sha256sum -c - >/dev/null) && echo 'resources verified'
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$(grep '  BixbyPromotion.dll$' $BK/v18d-files.sha256 | cut -d' ' -f1)" ] && echo 'DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V18D INSTALLED. Press Home to open the new Quiet Home.'
