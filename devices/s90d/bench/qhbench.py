#!/usr/bin/env python3
"""Quiet Home style benchmark helper.
  qhbench.py set  NAME glass shadow squircle   -> write /tmp/quiet-home-live.conf on the TV, kill Quiet Home
  qhbench.py read NAME                          -> collect state + memory, append to bench-results.json
"""
import os
import json, re, subprocess, sys, time
from pathlib import Path

SC = Path(os.environ.get("SG_BENCH_TMP", "/tmp/sg-bench"))  # where the runner scripts are written
OBS = Path(os.environ.get("SG_BENCH_OUT", ".local/bench"))  # the runner's output directory (S90D_OUT)
OUT = SC / "bench-results.json"


def run(label, body, t=60):
    script = SC / f"qhb-{label}.sh"
    script.write_text(body + "\necho '##### END'\n")
    lab = f"qhb-{label}-{int(time.time())}"
    subprocess.run(["python3", "-I", str(SC / "s90d-phaseB.py"), lab, str(script), str(t)], capture_output=True)
    return (OBS / f"{lab}.stdout").read_text()


cmd, name = sys.argv[1], sys.argv[2]
if cmd == "set":
    g, s, q = sys.argv[3:6]
    extra = "".join(f"\\n{kv}" for kv in sys.argv[6:])
    print(run(f"set-{name}", f"""printf 'glass={g}\\nshadow={s}\\nsquircle={q}\\nbench=60{extra}\\n' > /tmp/quiet-home-live.conf
chmod 644 /tmp/quiet-home-live.conf; command -v chsmack >/dev/null && chsmack -a '*' /tmp/quiet-home-live.conf
cat /tmp/quiet-home-live.conf | tr '\\n' ' '; echo
app_launcher -k com.samsung.tv.bixby-promotion 2>&1 | tail -n 1; sleep 1
app_launcher -s com.samsung.tv.bixby-promotion 2>&1 | tail -n 1; sleep 4; buxton2ctl get memory memory/appfw/current_fullscreen_appid | sed "s/.*= String: //"; grep -E "^style_conf=" /tmp/quiet-home-state.txt"""))
elif cmd == "measure":
    text = run(f"meas-{name}", r"""W=$(enlightenment_info -topvwins 2>&1 | awk '/SilentGlass Home|Quiet Home/{print $2; exit}')
echo "win=$W fg=$(buxton2ctl get memory memory/appfw/current_fullscreen_appid | sed 's/.*= String: //')"
echo "dvfs0=$(tr -d '\n' < /sys/kernel/debug/mali0/dvfs_utilization)"; s0=$(cut -d' ' -f1 /proc/uptime)
winfo -fps -win_id $W > /tmp/qhfps.txt 2>&1 & p=$!
sleep 10; kill $p 2>/dev/null
echo "dvfs1=$(tr -d '\n' < /sys/kernel/debug/mali0/dvfs_utilization)"; echo "secs=$s0 $(cut -d' ' -f1 /proc/uptime)"
sed 's/^/fps: /' /tmp/qhfps.txt | tail -n 14; rm -f /tmp/qhfps.txt""", 60)
    print(text)
    res = json.loads(OUT.read_text()) if OUT.exists() else []
    res.append({"name": name, "measure": text}); OUT.write_text(json.dumps(res, indent=1))
else:
    text = run(f"read-{name}", r"""P=$(app_launcher -S 2>/dev/null | sed -n 's/.*com.samsung.tv.bixby-promotion (\([0-9]*\)).*/\1/p')
echo "pid=$P"
grep -E '^(style|style_conf|blur_created|frame_stats|build_ms_by_page|key_count|key_ms_avg|key_ms_max|live|page)=' /tmp/quiet-home-state.txt
echo "rss_kb=$(sed -n 's/^VmRSS:\s*\([0-9]*\).*/\1/p' /proc/$P/status)"
echo "pss_kb=$(sed -n 's/^Pss:\s*\([0-9]*\).*/\1/p' /proc/$P/smaps_rollup)"
echo "gpu_kb=$(awk -v p=$P '$2==p{s+=$3} END{print s+0}' /sys/kernel/debug/mali0/gpu_memory)"
echo "gpu_total_kb=$(sed -n 's/.*gpu_mem_total\s*\([0-9]*\).*/\1/p' /sys/kernel/debug/mali0/gpu_memory)"
echo "mem_available_kb=$(sed -n 's/^MemAvailable:\s*\([0-9]*\).*/\1/p' /proc/meminfo)"
echo "cpu_ticks=$(awk '{print $14+$15}' /proc/$P/stat)" """)
    rec = {"name": name}
    for line in text.splitlines():
        if "=" in line and not line.startswith("#"):
            k, v = line.split("=", 1); rec[k] = v
    res = json.loads(OUT.read_text()) if OUT.exists() else []
    res.append(rec); OUT.write_text(json.dumps(res, indent=1))
    print(json.dumps(rec, indent=1))
