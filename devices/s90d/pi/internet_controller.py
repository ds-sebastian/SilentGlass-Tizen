#!/usr/bin/env python3
"""Paired-computer controller for the S90D Internet filter.

MQTT messages are wakeups only, never commands. The fixed script reads the TV's marker,
checks both protocol families, then writes a boot-bound confirmation for the UI.
Requires paho-mqtt 2.1.0 and the separately installed tv-access helper.
"""
import argparse
import base64
import json
import logging
import os
from pathlib import Path
import re
import subprocess
import threading
import time

LOG = logging.getLogger("silentglass-internet")


def command(helper, ip, script):
    # No MQTT payload or remote text is interpolated into a command.
    encoded = base64.b64encode(script.encode()).decode("ascii")
    return [helper, "--tv", ip, "--timeout", "90", 'eval "$(echo ' + encoded + ' | base64 -d)"']


def valid_request(value):
    return re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\|(on|off)", value) is not None


class Controller:
    def __init__(self, config):
        self.c = config
        self.wake = threading.Event()
        self.seen = None
        self.connected = False

    def message(self, topic, value):
        if topic == self.c["base"] + "/internet_request" and valid_request(value) and value != self.seen:
            self.seen = value
            self.wake.set()
        elif topic == self.c["base"] + "/availability" and value == "online":
            self.wake.set()

    def apply(self):
        script = Path(self.c["script"]).read_text()
        try:
            p = subprocess.run(command(self.c["tv_access"], self.c["tv_ip"], script),
                               capture_output=True, text=True, timeout=110)
            match = re.search(r"^SG_INTERNET=(on|off)$", p.stdout, re.M)
            if p.returncode or not match:
                # Do not log bootstrap output (may contain session credentials).
                LOG.warning("Filter check failed (exit %s); UI confirmation expires automatically", p.returncode)
                return False
            LOG.info("Verified Internet %s", "blocked" if match[1] == "off" else "allowed")
            return True
        except (OSError, subprocess.TimeoutExpired):
            LOG.warning("TV unreachable/busy; retrying without changing the requested state")
            return False

    def run(self):
        import paho.mqtt.client as mqtt
        client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="silentglass-internet-controller")
        client.username_pw_set(self.c.get("user"), self.c.get("password"))
        if self.c.get("tls"):
            client.tls_set(ca_certs=self.c.get("ca_cert"))
        def connected(c, userdata, flags, reason, properties):
            self.connected = reason == 0
            if self.connected:
                c.subscribe([(self.c["base"] + "/internet_request", 1), (self.c["base"] + "/availability", 1)])
                self.wake.set()
            else:
                LOG.warning("Broker refused the connection")
        client.on_connect = connected
        client.on_disconnect = lambda *args: setattr(self, "connected", False)
        def message(c, userdata, msg):
            if len(msg.payload) <= 64:
                self.message(msg.topic, msg.payload.decode("ascii", errors="replace"))
        client.on_message = message
        client.connect_async(self.c["host"], int(self.c.get("port", 1883)), 60)
        client.loop_start()
        # The marker on the TV is authoritative; MQTT only says when to look. A root session
        # runs on a new request (incl. a new boot id), every 10 min while blocked (drift), and
        # every 30 min when MQTT is down. Startup/reboot is not fail-closed: until the bridge
        # reconnects and this runs, the TV has Internet. A router-level block is needed for that.
        try:
            delay = 30
            while True:
                self.wake.clear()
                success = self.apply()
                delay = 30 if not success and delay < 30 else (min(delay * 2, 600) if not success else 0)
                if success:
                    blocked = (self.seen or "").endswith("|off")
                    wait = 600 if blocked else (None if self.connected else 1800)
                else:
                    wait = delay
                self.wake.wait(wait)
        finally:
            client.disconnect()
            client.loop_stop()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("config", type=Path)
    args = parser.parse_args()
    if args.config.stat().st_mode & 0o077:
        parser.error("config contains broker credentials; chmod 600 it")
    config = json.loads(args.config.read_text())
    config.setdefault("base", "silentglass/s90d")
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    Controller(config).run()


if __name__ == "__main__":
    main()
