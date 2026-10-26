#!/usr/bin/env python3
"""Prove the TV forwards LAN -> Thread: one ICMPv6 echo in an Ethernet frame addressed to the TV.

The frame's destination MAC is the TV, its IPv6 destination a Thread address behind it (for
example another border router's OMR address from `ot-ctl ipaddr`). A reply means the TV routed
the request into the mesh; confirm with the TV's `ip6tables -nvx -L OTBR_FORWARD_CHAIN`
eth0 -> wpan0 counter. Nothing on this host or the TV is reconfigured. Needs root (raw socket).

  sudo python3 probe_forwarding.py --iface eth0 --tv-mac 02:00:00:00:00:01 \\
      --src fd00:1::10 --dst fd00:2::5dec
"""
import argparse
import os
import socket
import struct
import sys
import time
from pathlib import Path


def checksum(data):
    if len(data) % 2:
        data += b"\0"
    s = sum(struct.unpack(f"!{len(data) // 2}H", data))
    while s >> 16:
        s = (s & 0xFFFF) + (s >> 16)
    return (~s) & 0xFFFF


def echo_frame(tv_mac, own_mac, src, dst, nonce):
    echo = struct.pack("!BBHHH", 128, 0, 0, os.getpid() & 0xFFFF, 1) + nonce
    pseudo = src + dst + struct.pack("!I3xB", len(echo), 58)
    echo = echo[:2] + struct.pack("!H", checksum(pseudo + echo)) + echo[4:]
    ipv6 = struct.pack("!IHBB16s16s", 6 << 28, len(echo), 58, 64, src, dst)
    return tv_mac + own_mac + b"\x86\xdd" + ipv6 + echo


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--iface", required=True, help="LAN interface on this host")
    ap.add_argument("--tv-mac", required=True, help="TV Ethernet MAC")
    ap.add_argument("--src", required=True, help="this host's routable IPv6 address on --iface")
    ap.add_argument("--dst", required=True, help="a Thread (mesh or OMR) address reachable through the TV")
    ap.add_argument("--timeout", type=float, default=8)
    a = ap.parse_args()
    tv_mac = bytes.fromhex(a.tv_mac.replace(":", ""))
    own_mac = bytes.fromhex(Path(f"/sys/class/net/{a.iface}/address").read_text().strip().replace(":", ""))
    src, dst = (socket.inet_pton(socket.AF_INET6, x) for x in (a.src, a.dst))
    nonce = b"quiet-tv-forward-test-" + os.urandom(12)
    sock = socket.socket(socket.AF_PACKET, socket.SOCK_RAW, socket.htons(0x86DD))
    sock.bind((a.iface, 0))
    sock.settimeout(a.timeout)
    start = time.monotonic()
    sock.send(echo_frame(tv_mac, own_mac, src, dst, nonce))
    try:
        while True:
            r, _ = sock.recvfrom(4096)
            if len(r) >= 62 and r[12:14] == b"\x86\xdd" and r[20] == 58 and r[22:38] == dst \
                    and r[38:54] == src and r[54] == 129 and r[62:] == nonce:
                print(f"PASS echo reply from {a.dst} via TV {a.tv_mac}; {(time.monotonic() - start) * 1000:.1f} ms")
                return 0
    except socket.timeout:
        print(f"FAIL no reply from {a.dst} within {a.timeout:g} s", file=sys.stderr)
        return 1
    finally:
        sock.close()


if __name__ == "__main__":
    sys.exit(main())
