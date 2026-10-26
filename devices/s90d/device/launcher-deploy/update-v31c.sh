#!/bin/sh
# Quiet Home v29b -> v31c (Home over the picture: Watch and Play bar, Inputs, Apps and Controls menus). Inverse: restore-v29b.sh.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
BK=/opt/usr/share/quiet-tizen-backups/s90d-home-v31c-20261025
ST=/home/owner/share/tmp/sdk_tools/s90d-home-v31c
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PREV=199564e9b93365d86484f2fc32f286b58ce46b324d379805f0eb0635bd753317
TARSHA=7fc4afbbfa91e179a8d6407a2bbb2abeb8d98a5fc63ac028accc2de05f6f6fcf
[ "$(id -u)" = 0 ]
[ ! -e "$BK" ] || { echo 'v31c backup dir exists - refusing'; exit 3; }
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ] || { echo 'installed DLL is not v29b - refusing'; exit 4; }
[ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
[ -d "$A/res/quiet-home" ]
[ "$(stat -c %d $A/res)" = "$(stat -c %d /opt/usr/share/quiet-tizen-backups)" ] || { echo 'backup is on another filesystem; mv would not preserve labels - refusing'; exit 7; }
[ "$(sha256sum $ST/v31c.tar.gz | cut -d' ' -f1)" = "$TARSHA" ] || { echo 'staged archive hash mismatch'; exit 6; }
dll_mode=$(stat -c %a $A/bin/BixbyPromotion.dll); dll_owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll)
echo "v29b DLL mode=$dll_mode owner=$dll_owner label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"

mkdir -m 700 "$BK"; mkdir "$BK/stage"
cp "$ST/v31c.tar.gz" "$ST/v31c-files.sha256" "$BK/"
tar -xzf "$BK/v31c.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../v31c-files.sha256 >/dev/null) && echo 'staged files verified'
app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true

echo '=== backup v29b'
cp -a "$A/bin/BixbyPromotion.dll" "$BK/v29b-BixbyPromotion.dll"
mv "$A/res/quiet-home" "$BK/v29b-quiet-home"
[ "$(sha256sum $BK/v29b-BixbyPromotion.dll | cut -d' ' -f1)" = "$PREV" ]

echo '=== install v31c'
cp "$BK/stage/BixbyPromotion.dll" "$A/bin/BixbyPromotion.dll"
chown "$dll_owner" "$A/bin/BixbyPromotion.dll"; chmod "$dll_mode" "$A/bin/BixbyPromotion.dll"; chsmack -a "$LBL" "$A/bin/BixbyPromotion.dll"
cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
find "$A/res/quiet-home" -exec chown 201:0 {} \;
find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;

echo '=== verify'
(cd "$A/res" && sed -n 's#  quiet-home/#  quiet-home/#p' "$BK/v31c-files.sha256" | sha256sum -c - >/dev/null) && echo 'resources verified'
[ "$(sha256sum $A/bin/BixbyPromotion.dll | cut -d' ' -f1)" = "$(grep '  BixbyPromotion.dll$' $BK/v31c-files.sha256 | cut -d' ' -f1)" ] && echo 'DLL verified'
echo "DLL mode=$(stat -c %a $A/bin/BixbyPromotion.dll) owner=$(stat -c %u:%g $A/bin/BixbyPromotion.dll) label=$(chsmack $A/bin/BixbyPromotion.dll | sed 's/.*access=//')"
bad=$(find "$A/res/quiet-home" -exec chsmack {} \; | grep -vc "access=\"$LBL\"" || true); echo "resource label mismatches: $bad"
rm -rf "$BK/stage" "$ST"
echo 'V31C INSTALLED. Press Home to open the new Quiet Home.'
