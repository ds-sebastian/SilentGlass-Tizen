# Prepare the optional host-controller feature. No network rules changed here. Inverse: uninstall.md.
set -eu
D=/opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data
B=/opt/usr/share/quiet-tizen-backups/internet-switch-$(date +%Y%m%dT%H%M%S)
[ ! -e "$D/internet-controller" ] || { echo already_prepared; exit 1; }
mkdir -m 700 "$B"
iptables -S > "$B/iptables.before"
ip6tables -S > "$B/ip6tables.before"
for n in internet-controller internet-off internet-state.txt; do [ ! -e "$D/$n" ] || cp -a "$D/$n" "$B/$n"; done
# Retire only our failed trial's privileged copy; nothing in firmware changed.
if [ -e /opt/usr/share/quiet-netoff/xtables ]; then
  setcap -r /opt/usr/share/quiet-netoff/xtables
  mv /opt/usr/share/quiet-netoff/xtables "$B/xtables-unsigned-trial"
fi
rmdir /opt/usr/share/quiet-netoff 2>/dev/null || :
printf 'paired-computer controller; startup is not fail-closed\n' > "$D/internet-controller"
chown owner:system_share "$D/internet-controller"
chmod 644 "$D/internet-controller"
chsmack -a User::Pkg::com.samsung.tv.bixby-promotion "$D/internet-controller"
echo "BACKUP=$B"
echo "INTERNET FEATURE PREPARED (Internet still allowed)"
