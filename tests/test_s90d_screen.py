"""Simulation of launcher/src/Screen.cs - what SilentGlass puts on screen - against a model of the S90D
(tests/screen-sim): SHIELD and Switch sleeping, waking over HDMI-CEC and resyncing, Ambient, standby, Samsung Home,
Home Assistant launches, the bar's idle close and the OLED dim. Scripted scenarios plus random runs under every
combination of the TV behaviours not yet measured. Fails on any broken rule: the wallpaper over a live picture, a bar
the remote cannot reach, SilentGlass over another app, Home leaving no working bar, Home with no picture and no
wallpaper."""
import os
import shutil, subprocess, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet")


@unittest.skipUnless(DOTNET, "needs the .NET 6 SDK")
class ScreenSimulation(unittest.TestCase):
    def test_no_rule_broken(self):
        out = ROOT / ".local/screen-sim"
        b = subprocess.run([DOTNET, "build", str(ROOT / "tests/screen-sim/ScreenSim.csproj"), "-c", "Release", "-o", str(out),
                            f"-p:BaseIntermediateOutputPath={ROOT / '.local/obj/ScreenSim'}/"],
                           capture_output=True, text=True, timeout=600)
        self.assertEqual(b.returncode, 0, b.stdout[-2000:])
        r = subprocess.run([DOTNET, str(out / "ScreenSim.dll"), os.environ.get("SG_SIM_RUNS", "300")], capture_output=True, text=True, timeout=900)
        self.assertEqual(r.returncode, 0, r.stdout[-6000:])
        self.assertIn("No rule broken.", r.stdout)


if __name__ == "__main__":
    unittest.main()
