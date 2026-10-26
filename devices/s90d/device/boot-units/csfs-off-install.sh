#!/bin/sh
# Install the owner unit that closes Samsung Home (csfs) while it sits unused in the background. Inverse: csfs-off-restore.sh
set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-csfs-off.service
usr(){ env HOME=$H XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ ! -e "$UD/$N" ] || { echo 'already installed'; exit 3; }
LBL=$(chsmack $H | sed 's/.*access="//; s/".*//')
base64 -d > "$UD/$N.new" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVNpbGVudEdsYXNzIEhvbWU6IGNsb3NlIFNhbXN1bmcgSG9tZSAoY3NmcywgfjEwMCBNQikgd2hpbGUgaXQgc2l0cyBpbiB0aGUgYmFja2dyb3VuZAoKW1NlcnZpY2VdClR5cGU9c2ltcGxlCiMgT25seSB3aGlsZSBTaWxlbnRHbGFzcyBIb21lIGlzIHRoZSBIb21lIGFwcCAoZGIvYXBwZncvaG9tZV9hcHBpZCkuIENoZWNrcyBvbmNlIGEgbWludXRlIGFuZCBjbG9zZXMgY3NmcyBvbmx5IGFmdGVyIGl0CiMgaGFzIGJlZW4gcnVubmluZyBvdXQgb2YgdmlldyBmb3IgdHdvIGNoZWNrcyBpbiBhIHJvdywgc28gU2Ftc3VuZyBIb21lIG9wZW5lZCBmcm9tIHRoZSBBcHBzIHRhYiBpcyBuZXZlciB0b3VjaGVkLgojIFNhbXN1bmcgcmVvcGVucyBpdCBub3cgYW5kIHRoZW4gKGUuZy4gdGhlIEFydCBhcHAgYWx3YXlzIHJldHVybnMgdG8gaXQpOyBpdCBpcyBzaW1wbHkgY2xvc2VkIGFnYWluLgojIElkZW50aXR5OiBhIHByb2Nlc3MgY291bnRzIGFzIFNhbXN1bmcgSG9tZSBvbmx5IHdpdGggaXRzIFNNQUNLIGxhYmVsICh0aGlzIHNjcmlwdCdzIG93biBjb21tYW5kIGxpbmUgY29udGFpbnMgdGhlCiMgc2VhcmNoIHRleHQsIGFuZCBhbiBlYXJsaWVyIHZlcnNpb24gZm91bmQgYW5kIGtpbGxlZCBpdHNlbGYpLiBMb2cgKGV2ZW50cyBvbmx5KTogL3J1bi91c2VyLzUwMDEvcXVpZXQtY3Nmcy1vZmYubG9nCkV4ZWNTdGFydD0vYmluL3NoIC1jICdleGVjID4+L3J1bi91c2VyLzUwMDEvcXVpZXQtY3Nmcy1vZmYubG9nIDI+JjE7IGVjaG8gIiQkKGRhdGUgKyUlVCkgc3RhcnQiOyBzZWVuPTA7IHdoaWxlIHNsZWVwIDYwOyBkbyBob21lPSQkKGJ1eHRvbjJjdGwgZ2V0IHN5c3RlbSBkYi9hcHBmdy9ob21lX2FwcGlkIDI+L2Rldi9udWxsIHwgc2VkICJzLy4qOiAvLyIpOyBwPTsgZm9yIHEgaW4gJCQocGdyZXAgLWYgIlsvXWNvbS5zYW1zdW5nLnR2LmNzZnMvIik7IGRvIFsgIiQkKGNhdCAvcHJvYy8kJHEvYXR0ci9jdXJyZW50IDI+L2Rldi9udWxsKSIgPSBVc2VyOjpQa2c6OmNvbS5zYW1zdW5nLnR2LmNzZnMgXSAmJiB7IHA9JCRxOyBicmVhazsgfTsgZG9uZTsgZmc9JCQoYnV4dG9uMmN0bCBnZXQgbWVtb3J5IG1lbW9yeS9hcHBmdy9jdXJyZW50X2Z1bGxzY3JlZW5fYXBwaWQgMj4vZGV2L251bGwgfCBzZWQgInMvLio6IC8vIik7IGlmIFsgIiQkaG9tZSIgPSBjb20uc2Ftc3VuZy50di5iaXhieS1wcm9tb3Rpb24gXSAmJiBbIC1uICIkJHAiIF0gJiYgWyAiJCRmZyIgIT0gY29tLnNhbXN1bmcudHYuY3NmcyBdOyB0aGVuIHNlZW49JCQoKHNlZW4rMSkpOyBpZiBbICQkc2VlbiAtZ2UgMiBdOyB0aGVuIGtpbGwgJCRwIDI+L2Rldi9udWxsICYmIGVjaG8gIiQkKGRhdGUgKyUlVCkgY2xvc2VkIGNzZnMgcGlkPSQkcCBmZz0kJGZnIGF2YWlsX2tiPSQkKGF3ayAiL01lbUF2YWlsYWJsZS97cHJpbnQgXCQkMn0iIC9wcm9jL21lbWluZm8pIjsgc2Vlbj0wOyBmaTsgZWxzZSBzZWVuPTA7IGZpOyBkb25lJwpSZXN0YXJ0PWFsd2F5cwpSZXN0YXJ0U2VjPTMwCgpbSW5zdGFsbF0KV2FudGVkQnk9ZGVmYXVsdC50YXJnZXQK
B64_EOF
[ "$(sha256sum "$UD/$N.new" | cut -d" " -f1)" = 5c916a539c79f8cf8a8ee1defa6f36b422f7736b260be024d09ee7b69bc42aac ]
chown owner:users "$UD/$N.new"; chmod 644 "$UD/$N.new"; chsmack -a "$LBL" "$UD/$N.new"; mv "$UD/$N.new" "$UD/$N"
ln -sf ../$N "$UD/default.target.wants/$N"; chown -h owner:users "$UD/default.target.wants/$N"
usr daemon-reload; usr start --no-block $N; sleep 3
echo "$N: $(usr is-enabled $N) $(usr is-active $N)"
echo 'CSFS WATCHER INSTALLED'
