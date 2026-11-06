"""The Internet switch: on the TV only. The bridge (owner user) sets the connected connman service to manual IPv4
with no gateway, records what it replaced first, and puts exactly that back. No shell, no firewall, nothing off the TV."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NET = (ROOT / "devices/s90d/device/ha-bridge/src/Internet.cs").read_text()
SHARED = (ROOT / "launcher/src/Shared.cs").read_text()


class InternetSwitch(unittest.TestCase):
    def test_no_shell_and_validated_arguments(self):
        self.assertNotIn("/bin/sh", NET)
        self.assertIn("psi.ArgumentList.Add", NET)
        self.assertIn('"/usr/bin/gdbus"', NET)
        self.assertIn('@"^/net/connman/service/[A-Za-z0-9_]+$"', NET)
        self.assertRegex(NET, r"!Ip4\.IsMatch\(addr\) \|\| !Ip4\.IsMatch\(mask\)")
        self.assertIn('method is not ("dhcp" or "manual")', NET)

    def test_records_before_changing_and_restores_what_it_replaced(self):
        off = NET[NET.index("if (!wanted) {"):NET.index("} else {", NET.index("if (!wanted) {"))]
        self.assertLess(off.index("File.WriteAllText(rec"), off.index('SetIpv4(path, "manual", addr, mask)'))
        self.assertIn("if (!File.Exists(rec))", off)  # the first record is never overwritten by a later re-apply
        on = NET[NET.index("} else {", NET.index("if (!wanted) {")):]
        self.assertIn('SetIpv4(p, "manual", r["address"], r["netmask"], r["gateway"])', on)  # an owner's static setup comes back
        self.assertIn("if (ok) File.Delete(rec);", on)

    def test_nothing_external(self):
        self.assertFalse((ROOT / "devices/s90d/device/internet").exists())
        self.assertFalse((ROOT / "devices/s90d/pi/internet_controller.py").exists())
        self.assertNotIn("iptables", NET)

    def test_status_reads_the_kernel_routes(self):
        self.assertIn('"/proc/net/route"', SHARED); self.assertIn('"/proc/net/ipv6_route"', SHARED)
        self.assertIn('f[0] != "lo"', SHARED); self.assertIn("(fl & 0x200) == 0", SHARED)  # not loopback, not reject routes
        self.assertEqual(SHARED.count("(fl & 3) == 3"), 2)  # through a gateway: the on-link default left when off doesn't count
        self.assertIn('"/proc/net/if_inet6"', SHARED)  # IPv6 only counts with a public address
        self.assertIn('return v4 ? "Applying" : v6 ? "IPv6 open" : "Blocked";', SHARED)


if __name__ == "__main__":
    unittest.main()
