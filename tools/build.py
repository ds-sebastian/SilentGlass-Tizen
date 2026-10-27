#!/usr/bin/env python3
"""Build locally; never redistribute Samsung reference assemblies."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=["launcher", "bridge"])
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    parser.add_argument("--framework", type=Path, default=ROOT / ".local/tv-framework")
    args = parser.parse_args()
    jobs = []
    if args.target == "launcher":
        for name in ("Tizen.NUI", "Tizen.Applications.Common", "Tizen.System", "Tizen"):
            if not (args.framework / f"{name}.dll").is_file():
                parser.error(f"Missing {name}.dll. Read docs/install.md (step 1); these are not bundled.")
        jobs.append((ROOT / "launcher/src/SilentGlass.csproj", "SilentGlass", "SilentGlass",
                     [f"-p:TV_FRAMEWORK_DIR={args.framework.resolve()}"]))
        jobs.append((ROOT / "launcher/probe/Probe.csproj", "Probe", "Probe", []))
    if args.target == "bridge":  # then: python3 tools/bridge_installer.py .local/build/ha-bridge
        jobs.append((ROOT / "devices/s90d/device/ha-bridge/src/HaBridge.csproj", "ha-bridge", "ha-bridge", []))
    for project, output, intermediate, extra in jobs:
        subprocess.run([
            args.dotnet, "build", str(project), "-c", "Release", "--nologo",
            "-o", str(ROOT / ".local/build" / output),
            f"-p:BaseIntermediateOutputPath={ROOT / '.local/obj' / intermediate}/",
            "-p:Deterministic=true",
            f"-p:PathMap={ROOT}=/_/quiet-tizen", *extra,
        ], check=True, env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
    if args.target == "launcher":
        destination = ROOT / ".local/package"
        destination.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / ".local/build/SilentGlass/BixbyPromotion.dll", destination / "BixbyPromotion.dll")
        shutil.copyfile(ROOT / ".local/build/Probe/Probe.dll", destination / "Probe.dll")
        assets = destination / "quiet-home"
        if assets.exists():
            shutil.rmtree(assets)
        shutil.copytree(ROOT / "launcher/assets", assets)
        local_catalog = ROOT / "launcher/assets/apps.local.json"
        if local_catalog.exists():
            shutil.copyfile(local_catalog, assets / "apps.json")
        (assets / "apps.local.json").unlink(missing_ok=True)
        print("Deployable files: .local/package/. Review the catalog and input mapping before use.")


if __name__ == "__main__":
    main()
