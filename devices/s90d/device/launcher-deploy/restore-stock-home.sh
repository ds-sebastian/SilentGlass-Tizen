#!/bin/sh
# Back to Samsung Home on the tested S90D, from any SilentGlass version: Home routing to the recorded originals, then
# the original Bixby promotion app (DLL + native image) from the first install's backup. SilentGlass's resources are
# moved into the backup folder, not deleted. Run as root via stdin. Registry flags are untouched (the install left
# them as they were). The TV's own rollback.sh accepts only the first trial build, so this one checks the backup
# instead: hashes, SMACK labels, ownership, and that SilentGlass is what's installed now.
set -eu
A=/opt/usr/apps/com.samsung.tv.bixby-promotion
B=/opt/usr/share/quiet-tizen-backups
D=$B/${1:-s90d-home-trial-20261024T123327}            # first install: original/ and its checks
R=$B/${2:-s90d-home-default-20261024}                 # Home routing: original-selectors.txt, Probe.dll
ORIGINAL=4ac11223f42bc520c0101a0ab369e3ac4899b48f9af437afce59d713793974b7
LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
PROBE="/usr/bin/dotnet $R/Probe.dll"
[ "$(id -u)" = 0 ]

echo '=== CHECK BACKUP'
[ "$(sha256sum "$D/original/bin/BixbyPromotion.dll" | cut -d' ' -f1)" = "$ORIGINAL" ]
[ "$(stat -c '%u:%g:%a' "$D/original/bin/BixbyPromotion.dll")" = 201:0:755 ]
(cd "$D/original"; sha256sum -c "$D/original-files.sha256" >/dev/null)
find "$D/original" -exec chsmack '{}' \; | sort > "$D/backup-smack-check.txt"
cmp -s "$D/backup-smack-check.txt" "$D/backup-smack-expected.txt"
[ -d "$D/original/bin/.native_image" ]
(cd "$R"; sha256sum -c files.sha256 >/dev/null)
o_op=$(sed -n 's/^main_operation=//p' "$R/original-selectors.txt")
o_home=$(sed -n 's/^home_appid=//p' "$R/original-selectors.txt")
for v in "$o_op" "$o_home"; do case "$v" in ''|*[!A-Za-z0-9_.-]*) echo 'bad recorded Home id'; exit 3;; esac; done
live=$(sha256sum "$A/bin/BixbyPromotion.dll" | cut -d' ' -f1)
[ "$live" != "$ORIGINAL" ] || { echo 'the original app is already installed - nothing to restore'; exit 0; }
[ -d "$A/res/quiet-home" ] || { echo 'SilentGlass resources not found: not a SilentGlass install - refusing'; exit 4; }
echo "backup OK; installed now: $live"

echo '=== HOME ROUTING'
$PROBE set-home "$o_op"
buxton2ctl set-string system db/appfw/home_appid "$o_home"
[ "$($PROBE | sed -n 's/^home_result=0 home=//p')" = "$o_op" ]
[ "$(buxton2ctl get system db/appfw/home_appid | sed 's/.*= String: //')" = "$o_home" ]
echo "Home -> $o_home (startup stays $(buxton2ctl get system db/appfw/startup_appid | sed 's/.*= String: //'))"

echo '=== ORIGINAL APP'
app_launcher -u 5001 -t com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
ts=$(date +%Y%m%dT%H%M%S)
mv "$A/res/quiet-home" "$D/silentglass-resources-$ts"
if [ -e "$A/bin/.native_image" ]; then mv "$A/bin/.native_image" "$D/silentglass-native-image-$ts"; fi
cp -a "$D/original/bin/.native_image" "$A/bin/.native_image"
for f in BixbyPromotion.dll BixbyPromotionTextResources.dll BixbyPromotionTextResources.pdb; do
  [ -e "$A/bin/.native_image/$f" ]; chsmack -a "$LBL" "$A/bin/.native_image/$f"
done
cp -a "$D/original/bin/BixbyPromotion.dll" "$A/bin/.restore-$ts.dll"
chsmack -a "$LBL" "$A/bin/.restore-$ts.dll"
mv "$A/bin/.restore-$ts.dll" "$A/bin/BixbyPromotion.dll"
[ "$(sha256sum "$A/bin/BixbyPromotion.dll" | cut -d' ' -f1)" = "$ORIGINAL" ]
[ "$(stat -c '%u:%g:%a' "$A/bin/BixbyPromotion.dll")" = 201:0:755 ]
(cd "$A"; sha256sum -c "$D/original-files.sha256" >/dev/null)
echo "STOCK HOME RESTORED. SilentGlass files kept in $D (silentglass-*-$ts). Press Home to check Samsung Home."
