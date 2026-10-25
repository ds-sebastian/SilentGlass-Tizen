# Quiet Home janitor (owner user unit, read via stdin). Never touches the app on screen; everything still opens normally.
# Phase 1, first 10 min of uptime: close Samsung's background boot preloads (Prime Video, Apple TV, browser) at once.
# Phase 2, afterwards, every 60 s: close Apple TV, Prime and the browser after 5 min in the background.
# Not listed (Samsung relaunches them within ~1 min, so closing only burns CPU): TV YouTube (litewebappservice),
# Samsung Home (com.samsung.tv.csfs) and its media rows (com.samsung.tv.home.media) - measured 2026-10-25.
L=/run/user/5001/quiet-prime-off.log; S=/run/user/5001/quiet-prime-off.seen
mkdir -p "$S"; exec >>"$L" 2>&1
up(){ cut -d. -f1 /proc/uptime; }
echo "start v5c up=$(up)"
while :; do
  u=$(up); r=$(app_launcher -S 2>/dev/null); fg=$(buxton2ctl get memory memory/appfw/current_fullscreen_appid 2>&1)
  if [ "$u" -lt 600 ]; then list="org.tizen.primevideo com.samsung.tv.aria-video org.tizen.browser"; grace=0
  else list="org.tizen.primevideo com.samsung.tv.aria-video org.tizen.browser"; grace=300; fi
  for a in $list; do
    f="$S/$a"
    case "$r" in *"$a ("*) ;; *) rm -f "$f"; continue;; esac
    case "$fg" in *"$a"*) rm -f "$f"; continue;; esac
    [ -f "$f" ] || echo "$u" > "$f"
    if [ $((u - $(cat "$f"))) -ge "$grace" ]; then app_launcher -k "$a" >/dev/null 2>&1; echo "up=$u closed background $a (fg: ${fg##* })"; rm -f "$f"; fi
  done
  if [ "$u" -lt 600 ]; then sleep 10; else sleep 60; fi
done
