#!/usr/bin/env python3
"""Build the release files from a clean checkout of a commit, so nothing local (photo wallpapers, app icons,
apps.local.json, the private device inventory) can slip in.

    python3 tools/release.py v1.0.0 [--commit HEAD] [--framework .local/tv-framework] [--dotnet dotnet]

Writes .local/dist/<version>/:
  silentglass-<version>.tar.gz   the bundle: launcher + Home Assistant bridge (stage it as quiet-tizen/package.tar.gz)
  install.sh                     installs or updates both from it, every file sha256-checked
  undo.sh                        puts back what one install replaced
  ha-bridge-restore.sh           removes the bridge
  SHA256SUMS
The version must be Shared.Version. It refuses if the committed install.sh doesn't list a fresh build of the
committed source (rebuild with tools/bundle.py, commit, release again).
The Samsung framework assemblies are only referenced at build time; they are never packaged.
"""
import argparse
import gzip
import hashlib
import io
import os
import re
import shutil
import subprocess
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FORBIDDEN = ("quiet-home/wallpapers/", "quiet-home/app-icons/", "quiet-home/apps.local.json")


def run(*cmd, cwd=None, env=None):
    subprocess.run([str(c) for c in cmd], cwd=cwd, check=True, env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1", **(env or {})})


def sha256(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def deterministic_tar(src: Path, dest: Path):
    """tar.gz of src's contents with fixed order, owner and times: the same tree always gives the same bytes."""
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w", format=tarfile.GNU_FORMAT) as tar:
        for path in sorted(src.rglob("*")):
            rel = "./" + path.relative_to(src).as_posix()
            info = tar.gettarinfo(str(path), rel)
            info.uid = info.gid = 0; info.uname = info.gname = ""; info.mtime = 0
            info.mode = 0o755 if path.is_dir() else 0o644
            if path.is_file():
                with path.open("rb") as f:
                    tar.addfile(info, f)
            else:
                tar.addfile(info)
    z = bytearray(gzip.compress(buf.getvalue(), 9, mtime=0))
    z[9] = 255  # the OS byte differs between Python versions; pin it
    dest.write_bytes(bytes(z))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("version")
    ap.add_argument("--commit", default="HEAD")
    ap.add_argument("--framework", type=Path, default=ROOT / ".local/tv-framework")
    ap.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    a = ap.parse_args()
    framework = a.framework.resolve()
    out = ROOT / ".local/dist" / a.version
    tree = ROOT / ".local/release-tree"
    if tree.exists():
        run("git", "worktree", "remove", "--force", tree, cwd=ROOT)
    run("git", "worktree", "add", "--detach", tree, a.commit, cwd=ROOT)
    try:
        run(sys.executable, "tools/build.py", "launcher", "--dotnet", a.dotnet, "--framework", framework, cwd=tree)
        run(sys.executable, "tools/build.py", "bridge", "--dotnet", a.dotnet, cwd=tree)
        ver = re.search(r'Version = "(\d+\.\d+\.\d+)"', (tree / "launcher/src/Shared.cs").read_text()).group(1)
        if a.version != "v" + ver:
            sys.exit(f"{a.version} is not Shared.Version (v{ver})")
        installer = tree / "devices/s90d/device/install.sh"
        committed = installer.read_bytes()
        run(sys.executable, "tools/bundle.py", "--out", tree / ".local/bundle", cwd=tree)
        if installer.read_bytes() != committed:
            sys.exit("The committed install.sh doesn't list a fresh build of this source: run tools/build.py launcher, "
                     "tools/build.py bridge and tools/bundle.py, commit install.sh, and release again.")
        package = tree / ".local/package"
        leaked = [p for p in (package.rglob("*")) if any(p.relative_to(package).as_posix().startswith(f) for f in FORBIDDEN)]
        if leaked:
            sys.exit("Local-only files in the package: " + ", ".join(str(p) for p in leaked[:5]))
        if out.exists():
            shutil.rmtree(out)
        out.mkdir(parents=True)
        shutil.copyfile(tree / f".local/bundle/silentglass-{ver}.tar.gz", out / f"silentglass-{a.version}.tar.gz")
        shutil.copyfile(installer, out / "install.sh")
        shutil.copyfile(installer.with_name("undo.sh"), out / "undo.sh")
        shutil.copyfile(tree / "devices/s90d/device/ha-bridge/ha-bridge-restore.sh", out / "ha-bridge-restore.sh")
        lines = [f"{sha256(p)}  {p.name}" for p in sorted(out.iterdir())]
        (out / "SHA256SUMS").write_text("\n".join(lines) + "\n", newline="\n")
        print("\n".join(lines))
        print(f"launcher BixbyPromotion.dll sha256 {sha256(package / 'BixbyPromotion.dll')}")
        print(f"-> {out}")
    finally:
        run("git", "worktree", "remove", "--force", tree, cwd=ROOT)


if __name__ == "__main__":
    main()
