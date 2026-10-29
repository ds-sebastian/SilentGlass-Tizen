#!/bin/sh
# Install or update SilentGlass (the launcher and its Home Assistant bridge) from one release bundle.
# Stage the release's silentglass-<version>.tar.gz on the TV as quiet-tizen/package.tar.gz, then, as root:
#   /bin/sh -s -- [launcher|bridge|all] < install.sh
# Default: the launcher, plus the bridge if it is already installed. "all" also installs the bridge the first time.
# The launcher part updates any installed SilentGlass; the first install is recipes/install-launcher.sh.
# Every file is checked against the list below before anything changes, and the files it replaces are kept in
# /opt/usr/share/quiet-tizen-backups/silentglass-<version>-<time>/. Undo: /bin/sh -s -- <that folder's name> < undo.sh
# The bridge's settings (silentglass-ha.conf) are never touched.
set -eu
VER=1.2.0
P=${SILENTGLASS_TEST_ROOT:-}   # tests only: a fake TV under this folder; empty on the TV
S=$P/home/owner/share/tmp/sdk_tools/quiet-tizen
A=$P/opt/usr/apps/com.samsung.tv.bixby-promotion; LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
H=$P/opt/usr/home/owner; UD=$H/.config/systemd/user; Q=$P/opt/usr/share/silentglass-ha; N=silentglass-ha.service
B=$P/opt/usr/share/quiet-tizen-backups
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
want=${1:-default}
case "$want" in default|launcher|bridge|all) ;; *) echo "usage: install.sh [launcher|bridge|all]"; exit 2;; esac
[ "$(id -u)" = 0 ]
[ -s "$S/package.tar.gz" ] || { echo "stage silentglass-$VER.tar.gz as $S/package.tar.gz first"; exit 3; }
do_launcher=0; do_bridge=0
[ "$want" = bridge ] || do_launcher=1
if [ "$want" = bridge ] || [ "$want" = all ] || { [ "$want" = default ] && [ -e "$Q/silentglass-ha.dll" ]; }; then do_bridge=1; fi
if [ $do_launcher = 1 ]; then
  [ -d "$A/res/quiet-home" ] && grep -q SilentGlass "$A/bin/BixbyPromotion.dll" ||
    { echo 'SilentGlass is not installed: do the first install with recipes/install-launcher.sh (docs/install.md) - refusing'; exit 4; }
  [ ! -e "$A/bin/.native_image" ] || { echo 'unexpected native image cache - refusing'; exit 5; }
fi

BK=$B/silentglass-$VER-$(date +%Y%m%dT%H%M%S)
[ ! -e "$BK" ] || { echo "$BK exists - refusing"; exit 6; }
mkdir -p "$B"; mkdir -m 700 "$BK"; mkdir "$BK/stage"
[ "$(stat -c %d "$A/res" 2>/dev/null || stat -c %d "$B")" = "$(stat -c %d "$B")" ] || { echo 'backup is on another filesystem; mv would not keep labels - refusing'; exit 7; }
cat > "$BK/files.sha256" <<'SUMS'
d32662e486d01570ab801ff2522399702c42f9e8418765ebe26cdb86d442f608  BixbyPromotion.dll
13e781d4e1f13494233e000124102caad8c9a5bb41bc244517f3bdcaea5ad730  Probe.dll
530d94cae62b04c4677ff4a6549f72b84c13115b13e16283fc2197a56442a275  bridge/silentglass-ha.conf.example
4647c9007b43c4dfe3ac2ded2f39ad245204940bd8b646e1f2bbd292be82ef93  bridge/silentglass-ha.dll
a0882085ebc8618322fb819cc0e92bac0499419af91124d05f6d27662818ea83  bridge/silentglass-ha.runtimeconfig.json
439b5d9d79665666e981a80dfe2450325464f996a2fddff48a9efef6627a7c42  bridge/silentglass-ha.service
236ead33439cced94720eb7a4e89afcc2dfeecf4b72de0dae3a97b024e700538  quiet-home/apps.json
291346f8cbcdaab61ffcc41f8ef5eb081cd58b52d34a7052af588c81d474d5b8  quiet-home/fonts/QuietInter-Display.ttf
779c520762e90955d45ea683cdf79fba2d885a00e7d181c410dec394cda95371  quiet-home/fonts/QuietInter-Medium.ttf
929b1bac76dc17aae42b61312fe27f59c5cfdac381bd74415107687221c23dfe  quiet-home/fonts/QuietInter-Semibold.ttf
7f9e1685fd5c8b7a9a302976cdfa461a3ad834563f894c279c29417340c3c46a  quiet-home/free-background.svg
490586fbc2d13037352861aacde543782091ebc3c3217644d593904ae3bd33d1  quiet-home/placeholders/card-0.svg
e3679958c8c453cac159b5b38c3f7ae31b91720b674865228e75c5f6242c9c65  quiet-home/placeholders/card-1.svg
357a3c2cb721002adbdc5de2b2e3bb044ad5a10514f5fb2fcd83798336d6593c  quiet-home/placeholders/card-10.svg
4bb7b8fea0d0792d8c515fa6be8f07329cb2adf77461981b0c70822ec7d7841b  quiet-home/placeholders/card-2.svg
559f6a956794cb39a54f50f4c27d3f6c20b5879d5161dc6672952617ce02c3d3  quiet-home/placeholders/card-3.svg
fd44df6b8d6b67ddb235c2183cc8667435be15e70ffc2f2f2bd4e39735fb95ef  quiet-home/placeholders/card-4.svg
bce1ecb2f7823c0ec6de625c3dafb13a882d048157ae4817df882e522d743c5f  quiet-home/placeholders/card-5.svg
b76a005407bc718831e51d2e66c55e8aac44cc92bcccd506bbe98d27574fb8a0  quiet-home/placeholders/card-6.svg
61b43409b0042bb10385b599481bd6b10cae5db3e8574893dbf03eb75ea9b14a  quiet-home/placeholders/card-7.svg
697923cdae0ebb490eda557ade98a08426a3f14bcafee7132f486f06c2952484  quiet-home/placeholders/card-8.svg
1fb61ee0e765bcc00b2601f76e9b499dff38299fbc89b9b1cbc6363788790b30  quiet-home/placeholders/card-9.svg
86f95aa0f22fbf5e15e37e9c715070928c6ff79f50f11caf00768919ecee06db  quiet-home/sym-antenna.svg
9975bde49b6d24453c1778426378200d34428ad0f551f872efcd05caaf12215a  quiet-home/sym-apps.svg
daddd56d4fbd52364bfc0caa84c2124a04338d168ce2eabee255f9134d49f1d7  quiet-home/sym-artframe.svg
7ee073b8834d6e7d002b26ac04e265524d9de23b6b732e964944b4f092639492  quiet-home/sym-bell.svg
5472ff74a9e8a4ba4cff139ca3524d5a49791c97a68920c87995f3884dc88e2d  quiet-home/sym-bluetooth.svg
6dfe243a92ef6f6595e2abd16e8be46c41b19b7cdc8ccb91063650fdb970dca7  quiet-home/sym-check.svg
3d5247a8fe4eb2c92be96f193d44b4ea26e6274c9d265d1367795b57587e8091  quiet-home/sym-chevron.svg
6b4dd1727197dbb2005ac65e42bf51d6fd4bb122f352309c936794f5c8b0e948  quiet-home/sym-contrast.svg
a2e10b4c2576e054ed97a5c2c85170e133f424d7adf2050a4517910b66a086ce  quiet-home/sym-controls.svg
b9580cdf968a2b6cb5291590d3730147161131ce3426b34aab5497e5a6d3c328  quiet-home/sym-gamecontroller.svg
2f74997d13e7a1403090f571d29746a87cadfb71ca49b2a3576adadc73523ada  quiet-home/sym-gear.svg
7aa22a2755bef6574599368cebad1bbcfc6f56874630690dedc503bb333d230b  quiet-home/sym-hdmi.svg
3baae4d8ac8301ee29ed08dcaede4448ede48a8d980c2377e9fa00c0d56a6aaa  quiet-home/sym-homeassistant.svg
e9edd3ce1cf8f506c2f5a34eef781a3541a7ea988550f50e8e22673e02748aa5  quiet-home/sym-info.svg
25fe4cf17e0b620322e4c95b38114b78acb6d99aa1161be12d5d47554a782994  quiet-home/sym-inputs.svg
78718ffcea7026869de7799c128cd8f602a46c9c2dceed235b726713165ea59e  quiet-home/sym-moon.svg
b987b27be9be7d49e92fc9cd9e7a8dcfa9f4316f6a556eed4732354400e53295  quiet-home/sym-mute.svg
7b40fbabe11d52042f3c87898fd9f753bc0eaea9dca206f928e61312ec4660d8  quiet-home/sym-network.svg
921921816f86e2d23c140281f739be6702994d75ed26d0cd033e0a1162ea60b2  quiet-home/sym-picture.svg
837d8fc6d46bd0f7cc457f0411162cff3a031b70a2700c098c936057abe06c5d  quiet-home/sym-pixelshift.svg
bac0bde9c94ea18850a62768df253a6cb5efaa050586cf62abb08f0970bbc5c3  quiet-home/sym-slidersmore.svg
c9421ea65502dc3b0cd5c1a950e9d8d78e2a77a35ed2ddef32cc9d35784e0d7c  quiet-home/sym-soundbar.svg
3e64d5f04c2e526b69ffd3199f5cb4acf77b3c808c41c0f664513d3110699e42  quiet-home/sym-speaker.svg
829fab62dcaa772b68b7b11bde371691065bfe5d50cb7fa4e67f6d2a22bed77c  quiet-home/sym-sun.svg
5d24fa47d1cc223f12794a7bb74ad1093f9f36568af04281bb9ed370bc652897  quiet-home/sym-thread.svg
2f92b6a386933993793d9d073c97636da4ec8ed33081f780563400b3e37e8f6d  quiet-home/sym-tv.svg
7db427a1c4aee77dc11f0f642e21bbe02349ccc6086bb573d669cebbf80a268b  quiet-home/sym-waveform.svg
1788a8052d8294fc85e0418d139fbcf21c7a4f8232650a895c9532985a73881e  quiet-home/tile-samsung-home.svg
SUMS
tar -xzf "$S/package.tar.gz" -C "$BK/stage"
(cd "$BK/stage" && sha256sum -c ../files.sha256 >/dev/null) || { echo "the staged bundle is not SilentGlass $VER - refusing (nothing changed)"; rm -rf "$BK"; exit 8; }
[ "$(cd "$BK/stage" && find . -type f | wc -l)" = "$(wc -l < "$BK/files.sha256")" ] || { echo 'the staged bundle has extra files - refusing (nothing changed)'; rm -rf "$BK"; exit 8; }
echo "SilentGlass $VER: bundle verified"
want_sha(){ grep "  $1\$" "$BK/files.sha256" | cut -d' ' -f1; }
have_sha(){ sha256sum "$1" 2>/dev/null | cut -d' ' -f1; }

if [ $do_launcher = 1 ]; then
  if [ "$(have_sha "$A/bin/BixbyPromotion.dll")" = "$(want_sha BixbyPromotion.dll)" ] &&
     (cd "$A/res" && grep '  quiet-home/' "$BK/files.sha256" | sha256sum -c - >/dev/null 2>&1) &&
     [ "$(find "$A/res/quiet-home" -type f | wc -l)" = "$(grep -c '  quiet-home/' "$BK/files.sha256")" ]; then
    echo "launcher: already $VER"
  else
    echo '=== launcher'
    dll_mode=$(stat -c %a "$A/bin/BixbyPromotion.dll"); dll_owner=$(stat -c %u:%g "$A/bin/BixbyPromotion.dll")
    app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
    cp -a "$A/bin/BixbyPromotion.dll" "$BK/launcher-BixbyPromotion.dll"
    mv "$A/res/quiet-home" "$BK/launcher-quiet-home"
    have_sha "$BK/launcher-BixbyPromotion.dll" > "$BK/launcher-previous.sha256"
    cp "$BK/stage/BixbyPromotion.dll" "$A/bin/.BixbyPromotion.dll.new"
    chown "$dll_owner" "$A/bin/.BixbyPromotion.dll.new"; chmod "$dll_mode" "$A/bin/.BixbyPromotion.dll.new"; chsmack -a "$LBL" "$A/bin/.BixbyPromotion.dll.new"
    mv "$A/bin/.BixbyPromotion.dll.new" "$A/bin/BixbyPromotion.dll"
    cp -R "$BK/stage/quiet-home" "$A/res/quiet-home"
    find "$A/res/quiet-home" -type d -exec chmod 755 {} \;
    find "$A/res/quiet-home" -type f -exec chmod 644 {} \;
    find "$A/res/quiet-home" -exec chown 201:0 {} \;
    find "$A/res/quiet-home" -exec chsmack -a "$LBL" {} \;
    (cd "$A/res" && grep '  quiet-home/' "$BK/files.sha256" | sha256sum -c - >/dev/null) &&
      [ "$(have_sha "$A/bin/BixbyPromotion.dll")" = "$(want_sha BixbyPromotion.dll)" ] ||
      { echo "LAUNCHER DOES NOT MATCH - undo: /bin/sh -s -- ${BK##*/} < undo.sh"; exit 9; }
    echo "launcher: $VER installed (was $(cut -c1-16 "$BK/launcher-previous.sha256")...)"
  fi
fi

if [ $do_bridge = 1 ]; then
  same=1
  for f in silentglass-ha.dll silentglass-ha.runtimeconfig.json; do [ "$(have_sha "$Q/$f")" = "$(want_sha "bridge/$f")" ] || same=0; done
  [ "$(have_sha "$UD/$N")" = "$(want_sha "bridge/$N")" ] || same=0
  if [ $same = 1 ]; then
    echo "bridge: already $VER"
  else
    echo '=== bridge'
    OL=$(chsmack "$H" | sed 's/.*access="//; s/".*//')
    if [ -d "$Q" ]; then cp -a "$Q" "$BK/bridge-files"; else : > "$BK/bridge-new"; fi
    if [ -e "$UD/$N" ]; then cp -a "$UD/$N" "$BK/bridge-unit"; fi
    [ -L "$UD/default.target.wants/$N" ] || : > "$BK/bridge-unlinked"
    mkdir -p "$Q" "$UD/default.target.wants"; chown root:users "$Q"; chmod 755 "$Q"; chsmack -a User "$Q"
    put(){ cp "$BK/stage/bridge/$1" "$2.new"; chown "$3" "$2.new"; chmod 644 "$2.new"; chsmack -a "$4" "$2.new"; mv "$2.new" "$2"; }
    put silentglass-ha.dll "$Q/silentglass-ha.dll" root:users User
    put silentglass-ha.runtimeconfig.json "$Q/silentglass-ha.runtimeconfig.json" root:users User
    put silentglass-ha.conf.example "$H/.config/silentglass-ha.conf.example" owner:users "$OL"
    put $N "$UD/$N" owner:users "$OL"
    ln -sf ../$N "$UD/default.target.wants/$N"; chown -h owner:users "$UD/default.target.wants/$N"
    usr daemon-reload
    usr restart $N; sleep 8; tail -n 3 /run/user/5001/silentglass-ha.log 2>/dev/null || true
    echo "bridge: $VER installed, $N $(usr is-enabled $N) $(usr is-active $N)"
  fi
fi

rm -rf "$BK/stage" "$S/package.tar.gz"
if [ -e "$BK/launcher-previous.sha256" ] || [ -e "$BK/bridge-files" ] || [ -e "$BK/bridge-new" ]; then
  for f in "$A/bin/BixbyPromotion.dll" "$Q/silentglass-ha.dll"; do if [ -e "$f" ]; then echo "$(have_sha "$f")  $f"; fi; done > "$BK/installed.sha256"
  echo "backup: $BK (undo: /bin/sh -s -- ${BK##*/} < undo.sh)"
else
  rm -rf "$BK"; echo 'nothing to change'
fi
echo "SILENTGLASS $VER INSTALLED"
