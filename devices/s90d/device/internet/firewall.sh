#!/bin/sh
# Run on stdin as root via tv-access. No arbitrary command input; the TV marker is authoritative.
# Default: reconcile. 'allow' removes only our hooks/chains (emergency inverse); never changes other rules.
# SILENTGLASS_TEST_ROOT is used only by offline tests.
set -eu
D=${SILENTGLASS_TEST_ROOT:-}/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data
mode=${1:-reconcile}
case "$mode" in reconcile|allow) ;; *) echo 'invalid mode' >&2; exit 2;; esac
[ -d "$D" ] || exit 3
boot=$(cat /proc/sys/kernel/random/boot_id)
up=$(cut -d' ' -f1 /proc/uptime)
state=error
report() {
  printf 'boot=%s\nup=%s\ninternet=%s\n' "$boot" "$up" "$state" > "$D/internet-state.txt.new"
  chown owner:system_share "$D/internet-state.txt.new"
  chmod 644 "$D/internet-state.txt.new"
  chsmack -a User::Pkg::com.samsung.tv.bixby-promotion "$D/internet-state.txt.new"
  mv "$D/internet-state.txt.new" "$D/internet-state.txt"
}
trap 'rc=$?; if [ "$rc" -ne 0 ]; then state=error; report || :; fi' EXIT
remove() {
  for hook in OUTPUT INPUT; do
    case "$hook" in OUTPUT) chain=SG_WAN_OUT;; INPUT) chain=SG_WAN_IN;; esac
    while "$t" -w 5 -C "$hook" -j "$chain" 2>/dev/null; do "$t" -w 5 -D "$hook" -j "$chain"; done
    if "$t" -w 5 -S "$chain" >/dev/null 2>&1; then "$t" -w 5 -F "$chain"; "$t" -w 5 -X "$chain"; fi
  done
}
rules() {
  # Exactly the same ordered rules are used to append and to verify with -C.
  "$t" -w 5 "$op" "$chain" "$iface" lo -j RETURN || return 1
  for net in $nets; do "$t" -w 5 "$op" "$chain" "$addr" "$net" -j RETURN || return 1; done
  "$t" -w 5 "$op" "$chain" -p tcp -j REJECT --reject-with tcp-reset || return 1
  "$t" -w 5 "$op" "$chain" -j REJECT || return 1
}
blocked() {
  for t in iptables ip6tables; do
    if [ "$t" = iptables ]; then
      nets='127.0.0.0/8 10.0.0.0/8 172.16.0.0/12 192.168.0.0/16 169.254.0.0/16 224.0.0.0/4 255.255.255.255/32'; count=10
    else nets='::1/128 fe80::/10 fc00::/7 ff00::/8'; count=7; fi
    for hook in OUTPUT INPUT; do
      case "$hook" in OUTPUT) chain=SG_WAN_OUT; iface=-o; addr=-d;; INPUT) chain=SG_WAN_IN; iface=-i; addr=-s;; esac
      op=-C
      n=$("$t" -w 5 -S "$chain" 2>/dev/null | wc -l) || n=0
      if [ "$n" -ne "$((count+1))" ] || ! rules; then
        # Detach only our hook before a rebuild; never touch Samsung/Thread chains.
        # Rule installation is not a zero-gap firewall during boot or repairs.
        while "$t" -w 5 -C "$hook" -j "$chain" 2>/dev/null; do "$t" -w 5 -D "$hook" -j "$chain"; done
        if "$t" -w 5 -S "$chain" >/dev/null 2>&1; then "$t" -w 5 -F "$chain"; else "$t" -w 5 -N "$chain"; fi
        op=-A; rules
      fi
      # Move only our jump to first position if Samsung moved its own hooks ahead of it.
      first=$("$t" -w 5 -S "$hook" | sed -n '2p')
      if [ "$first" != "-A $hook -j $chain" ]; then
        while "$t" -w 5 -C "$hook" -j "$chain" 2>/dev/null; do "$t" -w 5 -D "$hook" -j "$chain"; done
        "$t" -w 5 -I "$hook" 1 -j "$chain"
      fi
      op=-C; rules
      [ "$("$t" -w 5 -S "$chain" | wc -l)" -eq "$((count+1))" ]
      [ "$("$t" -w 5 -S "$hook" | sed -n '2p')" = "-A $hook -j $chain" ]
    done
  done
}
if [ "$mode" = allow ] || [ ! -f "$D/internet-off" ]; then
  for t in iptables ip6tables; do remove; done
  state=on
else blocked; state=off; fi
# If the owner changed their mind during the job, report only the actual rule state;
# Shared.InternetStatus shows Applying until the controller reconciles the new request.
report
printf 'SG_INTERNET=%s\nSG_BOOT=%s\n' "$state" "$boot"
