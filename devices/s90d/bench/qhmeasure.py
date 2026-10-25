#!/usr/bin/env python3
"""qhmeasure.py NAME [windows] - during Quiet Home's bench redraw: per 8 s window, compositor fps of the Quiet Home
window and GPU busy % (Mali DVFS counters, whole GPU). Appends raw text to bench-results.json."""
import json, re, subprocess, sys, time
from pathlib import Path

SC = Path("/home/dietpi/.hermes/cache/scratch"); OBS = Path("/home/dietpi/opt/samsung-tv-lab/observations/tv-cleanup-20261024")
name = sys.argv[1]; n = int(sys.argv[2]) if len(sys.argv) > 2 else 3
body = r"""W=$(enlightenment_info -topvwins 2>&1 | awk '/Quiet Home/{print $2; exit}')
echo "win=$W fg=$(buxton2ctl get memory memory/appfw/current_fullscreen_appid | sed 's/.*= String: //')"
echo "dvfs0=$(tr -d '\n' < /sys/kernel/debug/mali0/dvfs_utilization)"
i=1; while [ $i -le N ]; do
 winfo -fps -win_id $W > /tmp/qhfps.txt 2>&1 & p=$!
 sleep 8; kill $p 2>/dev/null
 echo "dvfs$i=$(tr -d '\n' < /sys/kernel/debug/mali0/dvfs_utilization)"
 sed "s/^/fps$i: /" /tmp/qhfps.txt | tail -n 9; rm -f /tmp/qhfps.txt; i=$((i+1))
done
echo '##### END'
""".replace("N ]", f"{n} ]")
script = SC / "qhmeasure.sh"; script.write_text(body)
lab = f"qhm-{name}-{int(time.time())}"
subprocess.run(["python3", "-I", str(SC / "s90d-phaseB.py"), lab, str(script), str(20 + 9 * n)], capture_output=True)
text = (OBS / f"{lab}.stdout").read_text()
b = [tuple(map(int, re.findall(r"busy_time: (\d+) idle_time: (\d+)", l)[0])) for l in text.splitlines() if l.startswith("dvfs")]
out = {"name": name, "gpu_busy": [], "fps": []}
for i in range(len(b) - 1):
    db, di = b[i + 1][0] - b[i][0], b[i + 1][1] - b[i][1]
    out["gpu_busy"].append(round(100 * db / (db + di), 1))
    f = [float(l.split("...")[-1]) for l in text.splitlines() if l.startswith(f"fps{i + 1}:")]
    out["fps"].append([min(f) if f else 0, round(sum(f) / len(f), 1) if f else 0, len(f)])
res = json.loads((SC / "bench-measure.json").read_text()) if (SC / "bench-measure.json").exists() else []
res.append(out); (SC / "bench-measure.json").write_text(json.dumps(res, indent=1))
print(text.splitlines()[0]); print(json.dumps(out))
