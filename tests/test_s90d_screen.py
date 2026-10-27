"""Simulation of launcher/src/Screen.cs - what SilentGlass puts on screen - against a model of the S90D
(tests/screen-sim): SHIELD and Switch sleeping, waking over HDMI-CEC and resyncing, Ambient, standby, Samsung Home,
Home Assistant launches, the bar's idle close and the OLED dim. Scripted scenarios plus random runs under every
combination of the TV behaviours not yet measured. Fails on any broken rule: the wallpaper over a live picture, a bar
the remote cannot reach, SilentGlass over another app, Home leaving no working bar, Home with no picture and no
wallpaper."""
import os, shutil, subprocess, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAB_DOTNET = "/home/dietpi/opt/samsung-tv-lab/dotnet6/dotnet"
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet") or (LAB_DOTNET if os.path.exists(LAB_DOTNET) else None)


@unittest.skipUnless(DOTNET, "needs the .NET 6 SDK")
class ScreenSimulation(unittest.TestCase):
    def test_no_rule_broken(self):
        out = ROOT / ".local/screen-sim"
        b = subprocess.run([DOTNET, "build", str(ROOT / "tests/screen-sim/ScreenSim.csproj"), "-c", "Release", "-o", str(out)],
                           capture_output=True, text=True, timeout=600)
        self.assertEqual(b.returncode, 0, b.stdout[-2000:])
        r = subprocess.run([DOTNET, str(out / "ScreenSim.dll"), os.environ.get("SG_SIM_RUNS", "300")], capture_output=True, text=True, timeout=900)
        self.assertEqual(r.returncode, 0, r.stdout[-6000:])
        self.assertIn("No rule broken.", r.stdout)

    def test_launcher_routes_through_screen(self):
        src = {p.name: p.read_text() for p in (ROOT / "launcher/src").glob("*.cs")}
        self.assertIn('<Compile Include="Screen.cs" />', (ROOT / "launcher/src/SilentGlass.csproj").read_text())
        self.assertIn("void Home(string open, string why) => screen.Home(open, why);", src["SilentGlass.cs"])
        self.assertIn("screen.Paused();", src["SilentGlass.cs"])
        self.assertIn("void SelectSource(int type) => screen.SelectSource(type);", src["Tv.cs"])
        for cb in ("screen.SourceChanged", "screen.SignalFlapped", "screen.Foreground(Foreground())", "screen.Standby()"):
            self.assertIn(cb, src["Tv.cs"])
        # the decisions live in Screen.cs only: nothing else shows the wallpaper or reads the signal flag itself
        for name, text in src.items():
            if name in ("Screen.cs", "Tv.cs"): continue
            self.assertNotIn("ShowBackdrop()", text.replace("void ShowBackdrop()", ""), name)
            self.assertNotIn("SignalKey", text, name)


if __name__ == "__main__":
    unittest.main()
