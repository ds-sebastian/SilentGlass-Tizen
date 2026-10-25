set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-prime-off.service
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ -f "$UD/$N" ]
cp -a "$UD/$N" /opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/$N.v4-before
base64 -d > "$UD/$N.new" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVF1aWV0IEhvbWU6IGNsb3NlIFByaW1lIFZpZGVvLCBBcHBsZSBUViBhbmQgdGhlIHdlYiBicm93c2VyIGFmdGVyIFNhbXN1bmcncyBib290IHByZWxvYWQgKG93bmVyIHJlcXVlc3QpCgpbU2VydmljZV0KVHlwZT1zaW1wbGUKIyBGb3IgdGhlIGZpcnN0IDEwIG1pbiBvZiB1cHRpbWU6IGlmIG9uZSBvZiB0aGVzZSBpcyBydW5uaW5nIGJ1dCBpcyBub3QgdGhlIGZ1bGxzY3JlZW4gYXBwLCBjbG9zZSBpdAojIChTYW1zdW5nIHByZWxvYWRzIHRoZW0gaW4gdGhlIGJhY2tncm91bmQgYXQgfjEtNSBtaW4pLiBBbGwgc3RheSBpbnN0YWxsZWQgYW5kIG9wZW4gbm9ybWFsbHkuIExvZyBpbiAvcnVuL3VzZXIvNTAwMS4KIyBUViBZb3VUdWJlIGlzIE5PVCBsaXN0ZWQ6IGxpdGV3ZWJhcHBzZXJ2aWNlIHJlbGF1bmNoZXMgaXQgaW1tZWRpYXRlbHkgd2hlbiBjbG9zZWQgKHJlc3RhcnQgY2h1cm4pLgpFeGVjU3RhcnQ9L2Jpbi9zaCAtYyAnTD0vcnVuL3VzZXIvNTAwMS9xdWlldC1wcmltZS1vZmYubG9nOyBleGVjID4+JCRMIDI+JjE7IHVwKCl7IGN1dCAtZC4gLWYxIC9wcm9jL3VwdGltZTsgfTsgZWNobyAic3RhcnQgdXA9JCQodXApIjsgd2hpbGUgWyAkJCh1cCkgLWx0IDYwMCBdOyBkbyByPSQkKGFwcF9sYXVuY2hlciAtUyAyPi9kZXYvbnVsbCk7IGZvciBhIGluIG9yZy50aXplbi5wcmltZXZpZGVvIGNvbS5zYW1zdW5nLnR2LmFyaWEtdmlkZW8gb3JnLnRpemVuLmJyb3dzZXI7IGRvIGNhc2UgIiQkciIgaW4gKiIkJGEgKCIqKSBmZz0kJChidXh0b24yY3RsIGdldCBtZW1vcnkgbWVtb3J5L2FwcGZ3L2N1cnJlbnRfZnVsbHNjcmVlbl9hcHBpZCAyPiYxKTsgY2FzZSAiJCRmZyIgaW4gKiIkJGEiKikgZWNobyAidXA9JCQodXApICQkYSBpcyBvbiBzY3JlZW4gLSBsZWF2aW5nIGl0Ijs7ICopIGFwcF9sYXVuY2hlciAtayAkJGEgPi9kZXYvbnVsbCAyPiYxOyBlY2hvICJ1cD0kJCh1cCkgY2xvc2VkIGJhY2tncm91bmQgJCRhIChmZzogJCR7ZmcjIyogfSkiOzsgZXNhYzs7IGVzYWM7IGRvbmU7IHNsZWVwIDEwOyBkb25lOyBlY2hvICJkb25lIHVwPSQkKHVwKSInCgpbSW5zdGFsbF0KV2FudGVkQnk9ZGVmYXVsdC50YXJnZXQK
B64_EOF
[ "$(sha256sum "$UD/$N.new" | cut -d' ' -f1)" = 62df32e79ae24ec4ff4b3436070687f28ef1c6fb59d2669f9549ce2ca86c5cdd ]
chown owner:users "$UD/$N.new"; chmod 644 "$UD/$N.new"; chsmack -a User::Home "$UD/$N.new"; mv "$UD/$N.new" "$UD/$N"
usr daemon-reload; echo "enabled: $(usr is-enabled $N)"
fg=$(buxton2ctl get memory memory/appfw/current_fullscreen_appid 2>&1); echo "fg: $fg"
for a in org.tizen.browser; do case "$fg" in *$a*) echo "$a on screen - leaving";; *) app_launcher -u 5001 -k $a 2>&1 | tail -n1;; esac; done
sleep 2; app_launcher -S 2>/dev/null | grep -E 'browser' || echo 'browser closed'
grep MemAvailable /proc/meminfo
echo 'CLOSE-AFTER-BOOT v4 INSTALLED'
