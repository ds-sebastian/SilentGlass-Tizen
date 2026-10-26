#!/bin/sh
set -eu
[ "$(id -u)" = 0 ]
rm -f /home/owner/apps_rw/com.samsung.tv.multiscreen/data/disable_preloading
echo 'MULTIVIEW PRELOAD RESTORED (from next boot)'
