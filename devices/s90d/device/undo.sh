#!/bin/sh
# Inverse of install.sh: puts back what one install replaced, from its backup folder. As root:
#   /bin/sh -s -- silentglass-<version>-<time> < undo.sh
# The launcher goes back to the backed-up build (checked against its recorded hash); the bridge goes back to its
# backed-up files, or is removed if that install added it. It refuses if either changed since that install. What it
# takes out is moved into the same backup folder (undone-*), not deleted. The bridge's settings (silentglass-ha.conf) are never touched.
set -eu
P=${SILENTGLASS_TEST_ROOT:-}   # tests only: a fake TV under this folder; empty on the TV
A=$P/opt/usr/apps/com.samsung.tv.bixby-promotion; LBL=User::Pkg::com.samsung.tv.bixby-promotion::RO
H=$P/opt/usr/home/owner; UD=$H/.config/systemd/user; Q=$P/opt/usr/share/silentglass-ha; N=silentglass-ha.service
B=$P/opt/usr/share/quiet-tizen-backups
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
have_sha(){ sha256sum "$1" 2>/dev/null | cut -d' ' -f1; }
name=${1:?the backup folder name install.sh printed, e.g. silentglass-1.2.0-20261030T120000}
case "$name" in silentglass-*) ;; *) echo 'not an install.sh backup - refusing'; exit 2;; esac
case "$name" in *[!A-Za-z0-9._-]*) echo 'bad name - refusing'; exit 2;; esac
BK=$B/$name
[ "$(id -u)" = 0 ]
[ -s "$BK/files.sha256" ] || { echo "$BK is not an install.sh backup - refusing"; exit 3; }
[ ! -e "$BK/undone" ] || { echo 'this install was already undone - refusing'; exit 3; }

if [ -e "$BK/launcher-previous.sha256" ]; then
  prev=$(cat "$BK/launcher-previous.sha256")
  [ "$(have_sha "$BK/launcher-BixbyPromotion.dll")" = "$prev" ] && [ -d "$BK/launcher-quiet-home" ] ||
    { echo 'the launcher backup is incomplete or damaged - refusing'; exit 4; }
  now=$(grep "  $A/bin/BixbyPromotion.dll\$" "$BK/installed.sha256" | cut -d' ' -f1)
  [ "$(have_sha "$A/bin/BixbyPromotion.dll")" = "$now" ] || { echo 'the launcher changed since this install - refusing'; exit 4; }
  echo '=== launcher'
  app_launcher -u 5001 -k com.samsung.tv.bixby-promotion >/dev/null 2>&1 || true
  if [ -d "$A/res/quiet-home" ]; then mv "$A/res/quiet-home" "$BK/undone-quiet-home"; fi
  cp -a "$BK/launcher-BixbyPromotion.dll" "$A/bin/.BixbyPromotion.dll.new"; chsmack -a "$LBL" "$A/bin/.BixbyPromotion.dll.new"
  mv "$A/bin/.BixbyPromotion.dll.new" "$A/bin/BixbyPromotion.dll"
  mv "$BK/launcher-quiet-home" "$A/res/quiet-home"
  [ "$(have_sha "$A/bin/BixbyPromotion.dll")" = "$prev" ] || { echo 'LAUNCHER RESTORE DOES NOT MATCH'; exit 9; }
  echo "launcher: restored ($(echo "$prev" | cut -c1-16)...)"
fi

if [ -d "$BK/bridge-files" ] || [ -e "$BK/bridge-new" ]; then
  echo '=== bridge'
  now=$(grep "  $Q/silentglass-ha.dll\$" "$BK/installed.sha256" | cut -d' ' -f1)
  [ "$(have_sha "$Q/silentglass-ha.dll")" = "$now" ] || { echo 'the bridge changed since this install - refusing'; exit 4; }
  usr stop $N 2>/dev/null || true
  if [ -d "$Q" ]; then mv "$Q" "$BK/undone-bridge"; fi
  if [ -d "$BK/bridge-files" ]; then
    mv "$BK/bridge-files" "$Q"
    if [ -e "$BK/bridge-unit" ]; then cp -a "$BK/bridge-unit" "$UD/$N.new"; mv "$UD/$N.new" "$UD/$N"; fi
    if [ -e "$BK/bridge-unlinked" ]; then rm -f "$UD/default.target.wants/$N"; fi
    usr daemon-reload; usr restart $N
    echo "bridge: restored, $N $(usr is-active $N 2>/dev/null || true)"
  else
    usr disable $N 2>/dev/null || true
    rm -f "$UD/default.target.wants/$N"
    if [ -e "$UD/$N" ]; then mv "$UD/$N" "$BK/undone-unit"; fi
    usr daemon-reload
    echo 'bridge: removed (this install had added it)'
  fi
fi
: > "$BK/undone"
echo 'SILENTGLASS INSTALL UNDONE'
