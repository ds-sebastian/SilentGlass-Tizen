#!/usr/bin/env python3
"""S90D drift check (read-only). Prints NOTHING when the TV still matches the expected state;
prints a short report when something drifted (e.g. after a Samsung firmware update) or the check
could not run. Expected values: s90d_expected.json next to this script.
Inspired by cleanyourtv's tools/check.py; changes nothing on the TV.
"""
import json, os, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
EXP = json.loads((HERE / "s90d_expected.json").read_text())
MISS = Path.home() / ".cache" / "s90d-drift-misses"
TV_ACCESS = os.environ.get("TV_ACCESS") or EXP.get("tv_access") or "tv-access"  # s90c-tools' helper

ids = " ".join(EXP["disabled_apps"])
keys = " ".join(EXP.get("settings", {}))
body = f"""
echo "up=$(cut -d. -f1 /proc/uptime)"
echo "build=$(sed -n 's/^Build=//p' /etc/prd_info.ini 2>/dev/null | cut -d';' -f1)"
echo "home=$(buxton2ctl get system db/appfw/home_appid | sed 's/.*= String: //')"
echo "startup=$(buxton2ctl get system db/appfw/startup_appid | sed 's/.*= String: //')"
echo "markers=$(ls /opt/usr/share/systemd/system 2>/dev/null | sort | tr '\\n' ',')"
DB=/opt/dbspace/.pkgmgr_parser.db
for a in {ids}; do echo "app $a $(sqlite3 -readonly $DB "select app_disable from package_app_info where app_id='$a';")"; done
echo "dll=$(sha256sum /opt/usr/apps/com.samsung.tv.bixby-promotion/bin/BixbyPromotion.dll | cut -d' ' -f1)"
U=/opt/usr/home/owner/.config/systemd/user/default.target.wants
echo "units=$(ls $U 2>/dev/null | tr '\\n' ',')"
echo "recog=$(systemctl show contents-recognition-service -p ActiveState --value)"
for k in {keys}; do echo "set $k $(vconftool get $k 2>/dev/null | sed -n 's/.*value = \\([^ ]*\\) .*/\\1/p')"; done
echo "##### END"
"""
cmd = "cat <<'PH_EOF' | /bin/sh\n" + body + "PH_EOF"
r = None
for _ in range(12):
    try:
        r = subprocess.run([TV_ACCESS, "--tv", EXP["tv"], "--timeout", "90", cmd], capture_output=True, timeout=170)
    except subprocess.TimeoutExpired:
        r = None; break
    if r.returncode == 125 and b"holds the local lock" in r.stderr:
        time.sleep(15); continue
    break
out = r.stdout.decode(errors="replace") if r else ""
if not r or r.returncode or "##### END" not in out:
    # TV in deep standby is normal: stay quiet unless it has been unreachable for 3 weekly runs in a row
    err = (r.stderr.decode(errors="replace")[-200:] if r else "timeout").strip()
    misses = int(MISS.read_text() or 0) + 1 if MISS.exists() else 1
    MISS.write_text(str(misses))
    if misses >= 3:
        print(f"S90D drift check could not reach the TV {misses} weeks in a row (off at the wall, offline, or Developer Mode off?): {err}")
    sys.exit(0)
MISS.unlink(missing_ok=True)

kv, apps, sets = {}, {}, {}
for line in out.splitlines():
    if line.startswith("set "):
        _, k, v = (line.split(" ", 2) + [""])[:3]; sets[k] = v.strip()
    elif line.startswith("app "):
        _, a, v = (line.split(" ", 2) + [""])[:3]; apps[a] = v.strip()
    elif "=" in line:
        k, v = line.split("=", 1); kv[k] = v.strip()

problems = []
def want(cond, msg):
    if not cond: problems.append(msg)

if kv.get("build") != EXP["build"]:
    problems.append(f"Firmware changed: {kv.get('build')} (was {EXP['build']}) - re-check everything below")
want(kv.get("home") == EXP["home_appid"], f"Home button no longer opens Quiet Home (home_appid={kv.get('home')})")
want(kv.get("startup") == EXP["startup_appid"], f"Power-on app changed (startup_appid={kv.get('startup')})")
have = set(filter(None, kv.get("markers", "").split(",")))
missing = sorted(set(EXP["markers"]) - have)
want(not missing, f"Vendor blocks missing: {', '.join(missing)}")
back = sorted(a for a in EXP["disabled_apps"] if apps.get(a) not in ("true", ""))
want(not back, f"Re-enabled apps: {', '.join(back)}")
want(kv.get("dll") in EXP["quiet_home_dlls"], "Quiet Home was replaced or changed (DLL hash differs)")
changed = sorted(k for k, v in EXP.get("settings", {}).items() if sets.get(k) != v)
want(not changed, "Update settings changed (automatic updates back on?): " + ", ".join(f"{k}={sets.get(k)}" for k in changed))
units = set(filter(None, kv.get("units", "").split(",")))
gone = sorted(set(EXP["user_units"]) - units)
want(not gone, f"Boot services missing: {', '.join(gone)}")
if int(kv.get("up", "0") or 0) > 900:
    want(kv.get("recog") in ("failed", "inactive"), f"Content recognition is running again (state={kv.get('recog')})")

if problems:
    print("S90D TV drift detected - nothing was changed:\n- " + "\n- ".join(problems))
    sys.exit(0)  # report is the stdout; non-zero would add a generic cron error alert
