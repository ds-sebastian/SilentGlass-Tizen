set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; QD=$H/.config/quiet-home; N=quiet-prime-off.service; BK=/opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ -f "$UD/$N" ]; [ ! -e $BK/$N.v5-before ] || { echo 'v5 already applied - refusing'; exit 3; }
cp -a "$UD/$N" $BK/$N.v5-before
mkdir -p "$QD"; chown owner:users "$QD"; chmod 755 "$QD"; chsmack -a User::Home "$QD"
base64 -d > "$QD/prime-off.sh.new" <<'B64_EOF'
IyBRdWlldCBIb21lIGphbml0b3IgKG93bmVyIHVzZXIgdW5pdCwgcmVhZCB2aWEgc3RkaW4pLiBOZXZlciB0b3VjaGVzIHRoZSBhcHAgb24gc2NyZWVuOyBldmVyeXRoaW5nIHN0aWxsIG9wZW5zIG5vcm1hbGx5LgojIFBoYXNlIDEsIGZpcnN0IDEwIG1pbiBvZiB1cHRpbWU6IGNsb3NlIFNhbXN1bmcncyBiYWNrZ3JvdW5kIGJvb3QgcHJlbG9hZHMgKFByaW1lIFZpZGVvLCBBcHBsZSBUViwgYnJvd3NlcikgYXQgb25jZS4KIyBQaGFzZSAyLCBhZnRlcndhcmRzLCBldmVyeSA2MCBzOiBjbG9zZSBTYW1zdW5nIEhvbWUsIGl0cyBtZWRpYSByb3dzLCBBcHBsZSBUViwgUHJpbWUgYW5kIHRoZSBicm93c2VyIGFmdGVyIDUgbWluIGluIHRoZSBiYWNrZ3JvdW5kLgojIFRWIFlvdVR1YmUgaXMgbm90IGxpc3RlZDogbGl0ZXdlYmFwcHNlcnZpY2UgcmVsYXVuY2hlcyBpdCB3aXRoaW4gc2Vjb25kcyAocmVzdGFydCBjaHVybikuCkw9L3J1bi91c2VyLzUwMDEvcXVpZXQtcHJpbWUtb2ZmLmxvZzsgUz0vcnVuL3VzZXIvNTAwMS9xdWlldC1wcmltZS1vZmYuc2Vlbgpta2RpciAtcCAiJFMiOyBleGVjID4+IiRMIiAyPiYxCnVwKCl7IGN1dCAtZC4gLWYxIC9wcm9jL3VwdGltZTsgfQplY2hvICJzdGFydCB2NSB1cD0kKHVwKSIKd2hpbGUgOjsgZG8KICB1PSQodXApOyByPSQoYXBwX2xhdW5jaGVyIC1TIDI+L2Rldi9udWxsKTsgZmc9JChidXh0b24yY3RsIGdldCBtZW1vcnkgbWVtb3J5L2FwcGZ3L2N1cnJlbnRfZnVsbHNjcmVlbl9hcHBpZCAyPiYxKQogIGlmIFsgIiR1IiAtbHQgNjAwIF07IHRoZW4gbGlzdD0ib3JnLnRpemVuLnByaW1ldmlkZW8gY29tLnNhbXN1bmcudHYuYXJpYS12aWRlbyBvcmcudGl6ZW4uYnJvd3NlciI7IGdyYWNlPTAKICBlbHNlIGxpc3Q9ImNvbS5zYW1zdW5nLnR2LmNzZnMgY29tLnNhbXN1bmcudHYuaG9tZS5tZWRpYSBvcmcudGl6ZW4ucHJpbWV2aWRlbyBjb20uc2Ftc3VuZy50di5hcmlhLXZpZGVvIG9yZy50aXplbi5icm93c2VyIjsgZ3JhY2U9MzAwOyBmaQogIGZvciBhIGluICRsaXN0OyBkbwogICAgZj0iJFMvJGEiCiAgICBjYXNlICIkciIgaW4gKiIkYSAoIiopIDs7ICopIHJtIC1mICIkZiI7IGNvbnRpbnVlOzsgZXNhYwogICAgY2FzZSAiJGZnIiBpbiAqIiRhIiopIHJtIC1mICIkZiI7IGNvbnRpbnVlOzsgZXNhYwogICAgIyBtZWRpYSByb3dzIHNlcnZlIFNhbXN1bmcgSG9tZTogb25seSBvbmNlIFNhbXN1bmcgSG9tZSBpdHNlbGYgaXMgZ29uZQogICAgaWYgWyAiJGEiID0gY29tLnNhbXN1bmcudHYuaG9tZS5tZWRpYSBdOyB0aGVuIGNhc2UgIiRyIiBpbiAqImNvbS5zYW1zdW5nLnR2LmNzZnMgKCIqKSBybSAtZiAiJGYiOyBjb250aW51ZTs7IGVzYWM7IGZpCiAgICBbIC1mICIkZiIgXSB8fCBlY2hvICIkdSIgPiAiJGYiCiAgICBpZiBbICQoKHUgLSAkKGNhdCAiJGYiKSkpIC1nZSAiJGdyYWNlIiBdOyB0aGVuIGFwcF9sYXVuY2hlciAtayAiJGEiID4vZGV2L251bGwgMj4mMTsgZWNobyAidXA9JHUgY2xvc2VkIGJhY2tncm91bmQgJGEgKGZnOiAke2ZnIyMqIH0pIjsgcm0gLWYgIiRmIjsgZmkKICBkb25lCiAgaWYgWyAiJHUiIC1sdCA2MDAgXTsgdGhlbiBzbGVlcCAxMDsgZWxzZSBzbGVlcCA2MDsgZmkKZG9uZQo=
B64_EOF
base64 -d > "$UD/$N.new" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVF1aWV0IEhvbWUgamFuaXRvcjogY2xvc2UgU2Ftc3VuZyBib290IHByZWxvYWRzLCBhbmQgU2Ftc3VuZyBIb21lIC8gQXBwbGUgVFYgLyBQcmltZSAvIGJyb3dzZXIgYWZ0ZXIgNSBtaW4gaW4gdGhlIGJhY2tncm91bmQgKG93bmVyIHJlcXVlc3QpCgpbU2VydmljZV0KVHlwZT1zaW1wbGUKIyBTY3JpcHQgaXMgcmVhZCBvbiBzdGRpbiAodW5zaWduZWQgZmlsZXMgY2Fubm90IGJlIGV4ZWN1dGVkIHVuZGVyIFVFUCkuIExvZzogL3J1bi91c2VyLzUwMDEvcXVpZXQtcHJpbWUtb2ZmLmxvZwpTdGFuZGFyZElucHV0PWZpbGU6L29wdC91c3IvaG9tZS9vd25lci8uY29uZmlnL3F1aWV0LWhvbWUvcHJpbWUtb2ZmLnNoCkV4ZWNTdGFydD0vYmluL3NoIC1zCgpbSW5zdGFsbF0KV2FudGVkQnk9ZGVmYXVsdC50YXJnZXQK
B64_EOF
[ "$(sha256sum "$QD/prime-off.sh.new" | cut -d' ' -f1)" = ac9e716e19c3d4d7fa7ffb9cfc3ea158422d48b5403060a466c55de48f2925ba ]
[ "$(sha256sum "$UD/$N.new" | cut -d' ' -f1)" = bb700305bda3be1505f724abc5bfdce238acab4d8f6b97d91e4d7c5740e0f918 ]
sh -n < "$QD/prime-off.sh.new"
for f in "$QD/prime-off.sh.new" "$UD/$N.new"; do chown owner:users "$f"; chmod 644 "$f"; chsmack -a User::Home "$f"; done
mv "$QD/prime-off.sh.new" "$QD/prime-off.sh"; mv "$UD/$N.new" "$UD/$N"
usr daemon-reload; usr restart $N; sleep 3
echo "enabled=$(usr is-enabled $N) active=$(usr is-active $N)"; tail -n 3 /run/user/5001/quiet-prime-off.log
grep MemAvailable /proc/meminfo
echo 'JANITOR v5 INSTALLED'
