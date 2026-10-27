#!/bin/sh
# Re-apply (2026-10-26): Samsung's app updates reinstall an app enabled (aov-dashboard came back with an update at
# 22:53 the evening batch E had disabled it). This re-disables any app on SilentGlass's privacy list (InfoStats.
# DisabledApps - batches C, E, F, F2) that is enabled again; nothing else. Each run keeps its own backup and record.
# Uses pkgcmd -D (the TV's own app disable). Run as root via stdin. Inverse: reapply-restore.sh (undoes the latest run)
set -eu
BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/reapply-$(date +%Y%m%dT%H%M%S); DB=/opt/dbspace/.pkgmgr_parser.db
HELPER="/usr/bin/dotnet /opt/usr/share/quiet-tizen-backups/privacy-phaseB-20261024T135601/AppMetadata.dll"
IDS="'com.samsung.tv.acr','com.samsung.tv.iacr','com.samsung.tv.contents-recognition-dbmgr','com.samsung.tv.contents-cognition-dbmgr','com.samsung.tv.farfieldvoice','com.samsung.tv.ondevice-voice','com.samsung.tv.voiceid-service','org.tizen.voice-client','org.tizen.stt-client','org.tizen.voice-app','org.tizen.voice-app-service','com.samsung.tv.wakeup-engine-bixby','com.samsung.tv.bixby-prov','com.samsung.tv.voiceassistant-music','com.samsung.tv.vif','com.samsung.tv.alexa-client','com.samsung.tv.alexa-fullscreen-app','com.samsung.tv.wakeup-engine-alexa','AQKO41xyKP.AmazonAlexa','HEPsqFNie0.tvplusstandalone','com.samsung.tv.targeted-ad-service','com.samsung.tv.utterance-recommender-core','com.samsung.tv.AICompanionAgent','com.samsung.tv.aov-dashboard','com.samsung.tv.home.media','com.samsung.tv.cobalt-yt','org.tizen.primevideo','org.tizen.primevideo-dummy','org.tizen.primevideo_companionapp','com.samsung.tv.aria-video','com.samsung.tv.aria-dummy','org.tizen.browser','com.samsung.tv.cis-api-service-app','com.samsung.tv.cis','com.samsung.tizen.samsung-analytics','org.tizen.multi-assistant-service','com.samsung.tv.aov-service','com.samsung.tv.voice-instant-app','org.tizen.voice-onboarding','com.samsung.tv.mybixby','com.samsung.tv.bixby-context-client','com.samsung.tv.bixbycapsuleviewer','adplatform.ADPlayer','com.samsung.tv.adplayer','com.samsung.tv.ad-web-framework'"
[ "$(id -u)" = 0 ]
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
sqlite3 -readonly $DB "select app_id from package_app_info where app_id in ($IDS) and app_disable='false';" > /tmp/reapply-todo.txt
[ -s /tmp/reapply-todo.txt ] || { echo 'nothing enabled again - nothing to do'; rm -f /tmp/reapply-todo.txt; exit 0; }
mkdir -m 700 $BK; mv /tmp/reapply-todo.txt $BK/flipped.txt
cp -a $DB $BK/registry-before.db; sha256sum $DB $BK/registry-before.db
echo "to disable: $(tr '\n' ' ' < $BK/flipped.txt)"
# through the package manager, not the registry alone: pkginfo-server caches app info, and an app it just updated
# keeps reading as enabled after a direct registry edit
for a in $(cat $BK/flipped.txt); do pkgcmd -D -t app -n $a --global 2>&1 | grep -E 'key\[end\]' || true; done
[ "$(sqlite3 -readonly $DB 'PRAGMA quick_check;')" = ok ]
$HELPER read-owner $(tr '\n' ' ' < $BK/flipped.txt)
for a in $(cat $BK/flipped.txt); do app_launcher -u 5001 -k $a >/dev/null 2>&1 || true; done
grep MemAvailable /proc/meminfo
echo "REAPPLIED: $BK"
