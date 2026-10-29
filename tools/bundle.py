#!/usr/bin/env python3
"""Build the one SilentGlass bundle (launcher + Home Assistant bridge) and the install.sh that checks it.

    python3 tools/build.py launcher && python3 tools/build.py bridge
    python3 tools/bundle.py [--public] [--out DIR] [--installer PATH]

Writes DIR/silentglass-<version>.tar.gz (default .local/bundle) and fills the version and the sha256 of every file
into install.sh (default: devices/s90d/device/install.sh, the committed one; a lab build with local assets passes
another path so the committed list stays the public one). --public leaves out the git-ignored local assets (photo
wallpapers, app icons, apps.local.json) the way a clean checkout builds. The version is Shared.Version. Same input,
same bytes.
"""
import argparse
import hashlib
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from release import deterministic_tar  # noqa: E402

TEMPLATE = ROOT / "devices/s90d/device/install.sh"
BRIDGE = ROOT / "devices/s90d/device/ha-bridge"
LOCAL = ("quiet-home/wallpapers/", "quiet-home/app-icons/")  # git-ignored (launcher/assets); never in a release
LIST = re.compile(r"(<<'SUMS'\n)(.*?)(\nSUMS\n)", re.S)


def version() -> str:
    return re.search(r'Version = "(\d+\.\d+\.\d+)"', (ROOT / "launcher/src/Shared.cs").read_text()).group(1)


def files(package: Path, bridge: Path, public: bool = False) -> dict:
    """bundle path -> source file"""
    out = {"BixbyPromotion.dll": package / "BixbyPromotion.dll", "Probe.dll": package / "Probe.dll"}
    for p in sorted((package / "quiet-home").rglob("*")):
        rel = p.relative_to(package).as_posix()
        if p.is_file() and not (public and rel.startswith(LOCAL)):
            out[rel] = p
    if public:  # the build copies apps.local.json over apps.json when it exists
        out["quiet-home/apps.json"] = ROOT / "launcher/assets/apps.json"
    for name, src in (("silentglass-ha.dll", bridge / "silentglass-ha.dll"),
                      ("silentglass-ha.runtimeconfig.json", bridge / "silentglass-ha.runtimeconfig.json"),
                      ("silentglass-ha.service", BRIDGE / "silentglass-ha.service"),
                      ("silentglass-ha.conf.example", BRIDGE / "silentglass-ha.conf.example")):
        out["bridge/" + name] = src
    missing = [k for k, v in out.items() if not v.is_file()]
    if missing:
        sys.exit("missing (build the launcher and the bridge first): " + ", ".join(missing))
    return out


def sums(entries: dict) -> str:
    return "\n".join(f"{hashlib.sha256(src.read_bytes()).hexdigest()}  {name}" for name, src in sorted(entries.items()))


def fill(template: str, ver: str, listing: str) -> str:
    text = re.sub(r"^VER=.*$", f"VER={ver}", template, count=1, flags=re.M)
    text, n = LIST.subn(lambda m: m.group(1) + listing + m.group(3), text)
    if n != 1:
        sys.exit("install.sh: the file list block was not found")
    return text


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--package", type=Path, default=ROOT / ".local/package")
    ap.add_argument("--bridge", type=Path, default=ROOT / ".local/build/ha-bridge")
    ap.add_argument("--out", type=Path, default=ROOT / ".local/bundle")
    ap.add_argument("--installer", type=Path, default=TEMPLATE)
    ap.add_argument("--public", action="store_true")
    a = ap.parse_args()
    ver, entries = version(), files(a.package, a.bridge, a.public)
    tree = a.out / "tree"
    if tree.exists():
        shutil.rmtree(tree)
    for name, src in entries.items():
        (tree / name).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(src, tree / name)
    a.out.mkdir(parents=True, exist_ok=True)
    tar = a.out / f"silentglass-{ver}.tar.gz"
    deterministic_tar(tree, tar)
    shutil.rmtree(tree)
    a.installer.write_text(fill(TEMPLATE.read_text(), ver, sums(entries)), newline="\n")  # runs on the TV: LF
    size = len(a.installer.read_bytes())
    if size >= 64 * 1024:
        sys.exit(f"{a.installer}: {size} bytes, over the TV runner's 64 KiB")
    print(f"{tar} ({len(entries)} files) and {a.installer} ({size} bytes), SilentGlass {ver}")


if __name__ == "__main__":
    main()
