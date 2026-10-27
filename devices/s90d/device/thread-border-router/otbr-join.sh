# Join an existing Thread network, or form a new one, on the TV's border router. Feed to /bin/sh on stdin as root,
# with the choice on the first line from a private file (never commit or print it):
#   DATASET=<hex>   active dataset of your existing network (Home Assistant: pi/thread_dataset.py; any OTBR:
#                   ot-ctl dataset active -x)
#   NEW=1           no Thread network yet: the TV forms one (then add it to Home Assistant's Thread integration)
#   FORCE=1         also replace a network the TV is already attached to
# e.g.  cat ~/.config/quiet-tizen/thread.env otbr-join.sh | <your root shell on the TV>
# The network is saved in /opt/var/lib/thread, so every later start (boot or the switch) rejoins it.
set -eu
C=/opt/usr/apps/com.samsung.tizen.smartthings-hub/bin/ot-ctl
[ "$(id -u)" = 0 ]
role(){ "$C" state 2>/dev/null | tr -d "\r" | head -n1; }
s=$(role); [ -n "$s" ] || { echo "border router not running (switched off on the phone page or in Home Assistant, or otbr-install.sh not run)"; exit 3; }
case "$s" in child|router|leader) [ "${FORCE:-0}" = 1 ] || { echo "already attached ($s) - FORCE=1 replaces the saved network"; exit 3; };; esac
if [ "${NEW:-0}" = 1 ]; then
  "$C" dataset init new | grep -q Done; "$C" dataset commit active | grep -q Done
elif [ -n "${DATASET:-}" ]; then
  printf '%s' "$DATASET" | grep -Eqx '([0-9a-fA-F]{2}){10,254}' || { echo "DATASET is not a hex TLV"; exit 2; }
  # load the TLVs into the dataset buffer, then commit that (older CLIs: set the active dataset directly)
  if "$C" dataset init tlvs "$DATASET" 2>/dev/null | grep -q Done; then "$C" dataset commit active | grep -q Done
  else "$C" dataset set active "$DATASET" | grep -q Done; fi
else
  echo "first line must set DATASET=<hex> or NEW=1"; exit 2
fi
DATASET=; unset DATASET
"$C" ifconfig up >/dev/null; "$C" thread start >/dev/null
i=0; while [ $i -lt 30 ]; do s=$(role); case "$s" in child|router|leader) echo "JOINED role=$s (becomes a router within ~2 min)"; exit 0;; esac; i=$((i+1)); sleep 2; done
echo "started, role=$s - check again in a minute (wrong dataset = stays detached)"; exit 1
