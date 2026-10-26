#!/usr/bin/env python3
"""Save Home Assistant's preferred Thread dataset as a private env file for otbr-join.sh (never printed).

  HA_URL=http://homeassistant.local:8123 HA_TOKEN=... python3 thread_dataset.py ~/.config/quiet-tizen/thread.env
  cat ~/.config/quiet-tizen/thread.env ../device/thread-border-router/otbr-join.sh | <root shell on the TV>

Uses HA's Thread websocket API (thread/list_datasets, thread/get_dataset_tlv). Standard library only.
"""
import base64
import json
import os
import socket
import ssl
import struct
import sys
from urllib.parse import urlparse


class Ws:
    """Just enough RFC 6455 for a few JSON request/response messages."""

    def __init__(self, url):
        u = urlparse(url)
        tls = u.scheme in ("https", "wss")
        port = u.port or (443 if tls else 80)
        raw = socket.create_connection((u.hostname, port), timeout=15)
        self.s = ssl.create_default_context().wrap_socket(raw, server_hostname=u.hostname) if tls else raw
        key = base64.b64encode(os.urandom(16)).decode()
        self.s.sendall((f"GET /api/websocket HTTP/1.1\r\nHost: {u.hostname}:{port}\r\nUpgrade: websocket\r\n"
                        f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
        head = b""
        while b"\r\n\r\n" not in head:
            chunk = self.s.recv(1024)
            if not chunk:
                raise SystemExit("websocket handshake failed")
            head += chunk
        if b" 101 " not in head.split(b"\r\n", 1)[0]:
            raise SystemExit("websocket handshake refused")
        self.buf = head.split(b"\r\n\r\n", 1)[1]

    def _read(self, n):
        while len(self.buf) < n:
            chunk = self.s.recv(65536)
            if not chunk:
                raise SystemExit("connection closed")
            self.buf += chunk
        out, self.buf = self.buf[:n], self.buf[n:]
        return out

    def send(self, obj):
        data = json.dumps(obj).encode()
        mask = os.urandom(4)
        n = len(data)
        head = bytes([0x81]) + (bytes([0x80 | n]) if n < 126 else bytes([0x80 | 126]) + struct.pack("!H", n))
        self.s.sendall(head + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))

    def recv(self):
        msg = b""
        while True:
            b0, b1 = self._read(2)
            n = b1 & 0x7F
            if n == 126:
                n = struct.unpack("!H", self._read(2))[0]
            elif n == 127:
                n = struct.unpack("!Q", self._read(8))[0]
            payload = self._read(n)
            if b0 & 0x0F in (0x1, 0x0):
                msg += payload
                if b0 & 0x80:
                    return json.loads(msg)


def preferred_tlv(ws, token):
    if ws.recv().get("type") != "auth_required":
        raise SystemExit("unexpected greeting")
    ws.send({"type": "auth", "access_token": token})
    if ws.recv().get("type") != "auth_ok":
        raise SystemExit("Home Assistant rejected the token")
    ws.send({"id": 1, "type": "thread/list_datasets"})
    datasets = ws.recv().get("result", {}).get("datasets", [])
    pick = next((d for d in datasets if d.get("preferred")), None)
    if not pick:
        raise SystemExit("Home Assistant has no preferred Thread network (Settings > Thread)")
    ws.send({"id": 2, "type": "thread/get_dataset_tlv", "dataset_id": pick["dataset_id"]})
    tlv = (ws.recv().get("result") or {}).get("tlv", "")
    if not tlv or any(c not in "0123456789abcdefABCDEF" for c in tlv):
        raise SystemExit("no dataset TLV returned")
    return pick.get("network_name", "?"), tlv


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    url, token = os.environ.get("HA_URL"), os.environ.get("HA_TOKEN")
    if not url or not token:
        raise SystemExit("set HA_URL and HA_TOKEN")
    name, tlv = preferred_tlv(Ws(url), token)
    path = os.path.expanduser(sys.argv[1])
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f:
        f.write(f"DATASET={tlv}\n")
    print(f"saved the dataset of Thread network {name!r} to {path} (mode 600)")


if __name__ == "__main__":
    main()
