set -eu
H=/opt/usr/home/owner; UD=$H/.config/systemd/user; N=quiet-recog-off.service
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }
[ "$(id -u)" = 0 ]; [ -f "$UD/$N" ]
cp -a "$UD/$N" /opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/$N.v5-before
base64 -d > "$UD/$N.new" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVF1aWV0IEhvbWU6IGtlZXAgY29udGVudHMtcmVjb2duaXRpb24gYW5kIGNvbnRlbnRzLXVpLWRldGVjdG9yIG9mZiAob3duZXIgcmVxdWVzdCAyMDI2LTEwLTI0KQoKW1NlcnZpY2VdClR5cGU9c2ltcGxlCiMgU2Ftc3VuZyBzdGFydHMgdGhlc2Ugc2V2ZXJhbCB0aW1lcyBkdXJpbmcgYm9vdCAoc3RhcnRlci50YXJnZXQsIG11bHRpLXVzZXIsIHZkdGl6ZW4tYm9vdHNlcS1kZWxheWVkOyB0aGUgZGV0ZWN0b3IKIyBSZXF1aXJlcz0gcmVjb2duaXRpb24gYW5kIHB1bGxzIGl0IGJhY2sgdXApLiBBIG9uZS1zaG90IGlzIG5vdCBlbm91Z2gsIHNvIHRoaXMgd2F0Y2hlczogZXZlcnkgMyBzIGZvciB0aGUgZmlyc3QKIyAxMCBtaW4gb2YgdXB0aW1lLCB0aGVuIGV2ZXJ5IDMwIHMuIEVhY2ggdGltZSBpdCBmaW5kcyBvbmUgcnVubmluZywgaXQga2lsbHMgaXQgdW50aWwgc3lzdGVtZCdzIHN0YXJ0IGxpbWl0IHN0b3BzIGl0LgojIExvZyAoZXZlbnRzIG9ubHkpOiAvcnVuL3VzZXIvNTAwMS9xdWlldC1yZWNvZy1vZmYubG9nCkV4ZWNTdGFydD0vYmluL3NoIC1jICdMPS9ydW4vdXNlci81MDAxL3F1aWV0LXJlY29nLW9mZi5sb2c7IGV4ZWMgPj4kJEwgMj4mMTsgdXAoKXsgY3V0IC1kLiAtZjEgL3Byb2MvdXB0aW1lOyB9OyBlY2hvICJzdGFydCB1cD0kJCh1cCkiOyB6YXAoKXsgaj0wOyB3aGlsZSBbICQkaiAtbHQgMTUgXTsgZG8gcD0kJChzeXN0ZW1jdGwgc2hvdyAkJDEgLXAgTWFpblBJRCAtLXZhbHVlIDI+L2Rldi9udWxsKTsgY2FzZSAiJCRwIiBpbiAiInwwfCpbITAtOV0qKSBicmVhazs7IGVzYWM7IGtpbGwgLTkgJCRwIDI+L2Rldi9udWxsOyBqPSQkKChqKzEpKTsgc2xlZXAgMC4zOyBkb25lOyBbICQkaiAtZ3QgMCBdICYmIGVjaG8gInVwPSQkKHVwKSAkJDEga2lsbHM9JCRqIHN0YXRlPSQkKHN5c3RlbWN0bCBzaG93ICQkMSAtcCBBY3RpdmVTdGF0ZSAtLXZhbHVlKSI7IH07IHdoaWxlIDo7IGRvIHphcCBjb250ZW50cy1yZWNvZ25pdGlvbi1zZXJ2aWNlOyB6YXAgY29udGVudHMtdWktZGV0ZWN0b3Itc2VydmljZTsgaWYgWyAkJCh1cCkgLWx0IDYwMCBdOyB0aGVuIHNsZWVwIDM7IGVsc2Ugc2xlZXAgMzA7IGZpOyBkb25lJwpSZXN0YXJ0PW9uLWZhaWx1cmUKCltJbnN0YWxsXQpXYW50ZWRCeT1kZWZhdWx0LnRhcmdldAo=
B64_EOF
[ "$(sha256sum "$UD/$N.new" | cut -d' ' -f1)" = 7a104f1fced534046953bb561cd96b4ee1063ba2741e3cd1bf4262794bf55a5d ]
chown owner:users "$UD/$N.new"; chmod 644 "$UD/$N.new"; chsmack -a User::Home "$UD/$N.new"; mv "$UD/$N.new" "$UD/$N"
usr daemon-reload
cp /run/user/5001/quiet-recog-off.log /opt/usr/share/quiet-tizen-backups/tv-cleanup-20261024/boot3-recog.log 2>/dev/null || true
rm -f /run/user/5001/quiet-recog-off.log
echo "before: $(systemctl show contents-recognition-service -p ActiveState --value)/$(systemctl show contents-ui-detector-service -p ActiveState --value)"
usr restart $N; sleep 15
echo "after watcher start: $(systemctl show contents-recognition-service -p ActiveState --value)/$(systemctl show contents-ui-detector-service -p ActiveState --value)"
echo "=== simulate the delayed boot target starting the detector again"
systemctl reset-failed contents-recognition-service contents-ui-detector-service; systemctl start contents-ui-detector-service 2>&1 | tail -n1 || true
sleep 20
echo "final: $(systemctl show contents-recognition-service -p ActiveState --value)/$(systemctl show contents-ui-detector-service -p ActiveState --value)"
ps -eo pid,comm | grep -E 'contents-recogn|contents-ui-de' || echo 'no recognition/detector processes'
echo "unit: $(usr show $N -p ActiveState -p SubState | tr '\n' ' ')"
echo "--- log"; cat /run/user/5001/quiet-recog-off.log
echo 'WATCHER INSTALLED'
