#!/bin/sh
# Multi View: Samsung's com.samsung.tv.multiscreen.service has ConditionPathExists=!.../data/disable_preloading, so this
# file stops the background preload at boot. Multi View still opens on demand (cold start). Inverse: multiview-restore.sh
set -eu
F=/home/owner/apps_rw/com.samsung.tv.multiscreen/data/disable_preloading
[ "$(id -u)" = 0 ]
[ ! -e $F ] || { echo 'already applied'; exit 3; }
systemctl cat com.samsung.tv.multiscreen.service | grep -q 'ConditionPathExists=!/home/owner/apps_rw/com.samsung.tv.multiscreen/data/disable_preloading' || { echo 'unit has no such condition on this firmware - refusing'; exit 4; }
: > $F; chown owner:system_share $F; chmod 644 $F; chsmack -a User::Pkg::com.samsung.tv.multiscreen $F
app_launcher -t com.samsung.tv.multiscreen >/dev/null 2>&1 || true
sleep 2; app_launcher -S 2>/dev/null | grep -q 'com.samsung.tv.multiscreen ' && echo 'multiscreen still running' || echo 'multiscreen stopped'
echo 'MULTIVIEW PRELOAD OFF'
