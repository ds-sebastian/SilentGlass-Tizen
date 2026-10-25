set -eu
H=/opt/usr/home/owner
UD=$H/.config/systemd/user
N=quiet-recog-off.service
ENV="XDG_RUNTIME_DIR=/run/user/5001 DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus;unix:path=/run/user/5001/dbus/user_bus_socket"
usr(){ env DBUS_SESSION_BUS_ADDRESS=kernel:path=/sys/fs/kdbus/5001-user/bus XDG_RUNTIME_DIR=/run/user/5001 HOME=$H systemctl --user "$@"; }  # root may use the user manager private socket
[ "$(id -u)" = 0 ]
LBL=$(chsmack $H | sed 's/.*access="//; s/".*//')
echo "home label: $LBL"
[ ! -e "$UD/default.target.wants/$N" ] || { echo 'already enabled - refusing'; exit 3; }
for d in $H/.config $H/.config/systemd $UD; do [ -d $d ] || { mkdir $d; chown owner:users $d; chmod 755 $d; chsmack -a "$LBL" $d; echo "created $d"; }; done
base64 -d > "$UD/$N" <<'B64_EOF'
W1VuaXRdCkRlc2NyaXB0aW9uPVF1aWV0IEhvbWU6IGtlZXAgY29udGVudHMtcmVjb2duaXRpb24gYW5kIGNvbnRlbnRzLXVpLWRldGVjdG9yIG9mZiAob3duZXIgcmVxdWVzdCAyMDI2LTEwLTI0KQoKW1NlcnZpY2VdClR5cGU9b25lc2hvdApUaW1lb3V0U3RhcnRTZWM9aW5maW5pdHkKIyBLaWxsIGVhY2ggc2VydmljZSB1bnRpbCBzeXN0ZW1kJ3Mgb3duIFN0YXJ0TGltaXRCdXJzdD01LzEwcyBtYXJrcyBpdCBmYWlsZWQgZm9yIHRoZSByZXN0IG9mIHRoaXMgYm9vdC4KIyBQSURzIGNvbWUgZnJvbSBzeXN0ZW1kIG92ZXIgRC1CdXMgKHRoaXMgc2Vzc2lvbiBjYW5ub3QgcmVhZCBTeXN0ZW06OlByaXZpbGVnZWQgL3Byb2MgZW50cmllcykuCkV4ZWNTdGFydD0vYmluL3NoIC1jICdmb3IgVSBpbiBjb250ZW50cy1yZWNvZ25pdGlvbi1zZXJ2aWNlIGNvbnRlbnRzLXVpLWRldGVjdG9yLXNlcnZpY2U7IGRvIG49MDsgaT0wOyB3aGlsZSBbICQkaSAtbHQgMTIwMCBdOyBkbyBpPSQkKChpKzEpKTsgcz0kJChzeXN0ZW1jdGwgc2hvdyAkJFUgLXAgQWN0aXZlU3RhdGUgLS12YWx1ZSAyPi9kZXYvbnVsbCk7IFsgIiQkcyIgPSBmYWlsZWQgXSAmJiBicmVhazsgcD0kJChzeXN0ZW1jdGwgc2hvdyAkJFUgLXAgTWFpblBJRCAtLXZhbHVlIDI+L2Rldi9udWxsKTsgaWYgWyAtbiAiJCRwIiBdICYmIFsgIiQkcCIgIT0gMCBdOyB0aGVuIGtpbGwgLTkgJCRwIDI+L2Rldi9udWxsICYmIG49JCQoKG4rMSkpOyBzbGVlcCAwLjM7IGVsc2Ugc2xlZXAgMC41OyBmaTsgZG9uZTsgZWNobyAiJCRVOiBraWxscz0kJG4gc3RhdGU9JCQoc3lzdGVtY3RsIHNob3cgJCRVIC1wIEFjdGl2ZVN0YXRlIC0tdmFsdWUpIjsgZG9uZScKCltJbnN0YWxsXQpXYW50ZWRCeT1kZWZhdWx0LnRhcmdldAo=
B64_EOF
[ "$(sha256sum "$UD/$N" | cut -d' ' -f1)" = d21cea4476c31d924e33019d4109dfdd881004633e347bd7e783c63b12300426 ]
chown owner:users "$UD/$N"; chmod 644 "$UD/$N"; chsmack -a "$LBL" "$UD/$N"
usr daemon-reload
[ -d $UD/default.target.wants ] || { mkdir $UD/default.target.wants; chown owner:users $UD/default.target.wants; chmod 755 $UD/default.target.wants; chsmack -a "$LBL" $UD/default.target.wants; }
ln -s ../$N $UD/default.target.wants/$N; chown -h owner:users $UD/default.target.wants/$N
usr daemon-reload
echo "enabled: $(usr is-enabled $N 2>&1)"
ls -la $UD $UD/default.target.wants
echo "=== start now (test)"; date +%T
usr start $N
date +%T
for U in contents-recognition-service contents-ui-detector-service; do echo "$U: $(systemctl show $U -p ActiveState -p SubState -p NRestarts -p Result | tr '\n' ' ')"; done
usr status $N --no-pager 2>&1 | tail -n 6
echo 'INSTALLED'
