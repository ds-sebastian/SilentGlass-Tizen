set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-prime-off.service
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]
[ ! -e "$UD/$N" ] || { echo 'already installed - refusing'; exit 3; }
base64 -d > "$UD/$N" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVF1aWV0IEhvbWU6IGNsb3NlIFByaW1lIFZpZGVvIGFmdGVyIFNhbXN1bmcncyBib290IHByZWxvYWQgKG93bmVyIHJlcXVlc3QgMjAyNi0xMC0yNCkKCltTZXJ2aWNlXQpUeXBlPXNpbXBsZQojIEZvciB0aGUgZmlyc3QgMTAgbWluIG9mIHVwdGltZTogaWYgUHJpbWUgaXMgcnVubmluZyBidXQgaXMgbm90IHRoZSBmdWxsc2NyZWVuIGFwcCwgY2xvc2UgaXQgKFNhbXN1bmcncyB3ZWItYXBwCiMgcHJlbG9hZGVyIHN0YXJ0cyBpdCBpbiB0aGUgYmFja2dyb3VuZCBhdCB+My01IG1pbiwgaWdub3JpbmcgZGlzYWJsZS5hcHBib290aW5nKS4gTG9nIGluIC9ydW4vdXNlci81MDAxLgpFeGVjU3RhcnQ9L2Jpbi9zaCAtYyAnTD0vcnVuL3VzZXIvNTAwMS9xdWlldC1wcmltZS1vZmYubG9nOyBleGVjID4+JCRMIDI+JjE7IHVwKCl7IGN1dCAtZC4gLWYxIC9wcm9jL3VwdGltZTsgfTsgZWNobyAic3RhcnQgdXA9JCQodXApIjsgd2hpbGUgWyAkJCh1cCkgLWx0IDYwMCBdOyBkbyBpZiBhcHBfbGF1bmNoZXIgLVMgMj4vZGV2L251bGwgfCBncmVwIC1xICJvcmcudGl6ZW4ucHJpbWV2aWRlbyAoIjsgdGhlbiBmZz0kJChidXh0b24yY3RsIGdldCBtZW1vcnkgbWVtb3J5L2FwcGZ3L2N1cnJlbnRfZnVsbHNjcmVlbl9hcHBpZCAyPiYxKTsgY2FzZSAiJCRmZyIgaW4gKnByaW1ldmlkZW8qKSBlY2hvICJ1cD0kJCh1cCkgcHJpbWUgaXMgb24gc2NyZWVuIC0gbGVhdmluZyBpdCI7OyAqKSBhcHBfbGF1bmNoZXIgLWsgb3JnLnRpemVuLnByaW1ldmlkZW8gPi9kZXYvbnVsbCAyPiYxOyBlY2hvICJ1cD0kJCh1cCkgY2xvc2VkIGJhY2tncm91bmQgcHJpbWUgKGZnOiAkJHtmZyMjKiB9KSI7OyBlc2FjOyBmaTsgc2xlZXAgMTA7IGRvbmU7IGVjaG8gImRvbmUgdXA9JCQodXApIicKCltJbnN0YWxsXQpXYW50ZWRCeT1kZWZhdWx0LnRhcmdldAo=
B64_EOF
[ "$(sha256sum "$UD/$N" | cut -d' ' -f1)" = 162e5d73988432f19a5598a8351a78b48dabca649df55d9b58dce4ad389dce9a ]
chown owner:users "$UD/$N"; chmod 644 "$UD/$N"; chsmack -a User::Home "$UD/$N"
ln -s ../$N $UD/default.target.wants/$N; chown -h owner:users $UD/default.target.wants/$N
usr daemon-reload; echo "enabled: $(usr is-enabled $N)"
rm -f /run/user/5001/quiet-prime-off.log; usr start $N; sleep 3
echo "unit: $(usr show $N -p ActiveState -p Result | tr '\n' ' ')"; cat /run/user/5001/quiet-prime-off.log
ls -la $UD/default.target.wants/
echo 'PRIME-OFF INSTALLED'
