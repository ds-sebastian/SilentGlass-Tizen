#!/usr/bin/env python3
"""Refresh the embedded files of devices/s90d/device/ha-bridge/ha-bridge-install.sh (base64 blocks + sha256 guards)
from a bridge build and the unit/example next to the installer.

    python3 tools/build.py bridge          (reproducible: the same source gives the same bytes anywhere)
    python3 tools/bridge_installer.py .local/build/ha-bridge
"""
import base64
import hashlib
import lzma
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIR = ROOT / "devices/s90d/device/ha-bridge"
INSTALLER = DIR / "ha-bridge-install.sh"


def packed(data: bytes) -> bytes:
    """xz (the TV has /usr/bin/xz): ~13% smaller than gzip, which keeps the installer under the TV runner's 64 KiB.
    No timestamps or OS byte in the format, so the same DLL gives the same installer anywhere (CI checks this)."""
    return lzma.compress(data, format=lzma.FORMAT_XZ, check=lzma.CHECK_CRC32, preset=9 | lzma.PRESET_EXTREME)


def main():
    build = Path(sys.argv[1])
    # target file name in the installer -> source
    sources = {
        "silentglass-ha.dll": build / "silentglass-ha.dll",
        "silentglass-ha.runtimeconfig.json": build / "silentglass-ha.runtimeconfig.json",
        "silentglass-ha.conf.example": DIR / "silentglass-ha.conf.example",
        "$N": DIR / "silentglass-ha.service",
    }
    text = INSTALLER.read_text()
    for name, src in sources.items():
        data = src.read_bytes()
        # the DLL travels xz-packed (the TV runner takes at most 64 KiB per command); the hash is of the real file
        payload = packed(data) if name.endswith(".dll") else data
        b64 = base64.b64encode(payload).decode() + "\n"  # one line per file
        digest = hashlib.sha256(data).hexdigest()
        pat = re.compile(r'(base64 -d (?:<<\'B64_EOF\' \| xz -dc )?> "[^"]*' + re.escape(name) + r'\.new"(?: <<\'B64_EOF\')?\n)(.*?)(B64_EOF\n\[ "\$\(sha256sum "[^"]*'
                         + re.escape(name) + r'\.new" \| cut -d" " -f1\)" = )([0-9a-f]{64})( \])', re.S)
        text, n = pat.subn(lambda m: m.group(1) + b64 + m.group(3) + digest + m.group(5), text)
        if n != 1:
            sys.exit(f"{name}: {n} blocks matched")
        print(f"{name}: {len(data)} bytes {digest[:16]}")
    INSTALLER.write_text(text, newline="\n")  # runs on the TV: LF even when built on Windows


if __name__ == "__main__":
    main()
