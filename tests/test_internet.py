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

    def test_failsafe_gives_the_tv_its_own_settings_back(self):
        # off pins an address with no DHCP; if the home network changes, the TV must not stay unreachable
        self.assertIn("const long FailsafeMs = 10 * 60_000;", NET)
        watch = NET[NET.index("static void WatchHomeNetwork"):NET.index("static bool Answers")]
        self.assertLess(watch.index("File.WriteAllText(hold"), watch.index("PutBack(rec, path)"))  # the hold is recorded first
        self.assertIn("if (path == null) return;", watch)  # a cable pulled out is not a changed network
        self.assertIn('"\\nrouter=" + Field(cur, "Gateway")', NET)  # the router to watch is recorded when switching off
        rec = NET[NET.index("static void ReconcileInternet"):NET.index("static bool PutBack")]
        self.assertIn("if (wanted && File.Exists(hold)) File.Delete(hold);", rec)
        self.assertIn("if (!Answers(RouteGateway())) return;", rec)  # off again only once the home network answers
        self.assertIn('"Open · home network lost"', SHARED)

    def test_home_guard_keeps_samsungs_network_mark_off(self):
        guard = NET[NET.index("static void GuardHome"):NET.index("static string Sqlite")]
        self.assertIn("update widgetinfo set is_network=0 where tizen_id='\" + Shared.Self + \"' and is_network<>0", guard)
        self.assertIn('"/usr/bin/sqlite3"', NET)
        self.assertIn('".timeout 5000"', NET)  # Samsung's service holds the database briefly
        self.assertIn('public const string WasDb = "/opt/dbspace/.webappservice.db";', SHARED)
        self.assertIn("StartHomeGuard();", (ROOT / "devices/s90d/device/ha-bridge/src/Bridge.cs").read_text())

    def test_clock_offset_only_while_the_tv_clock_is_unset(self):
        self.assertIn("public static bool ClockUnset(DateTime utc) => utc.Year < 2020;", SHARED)
        self.assertIn("if (!ClockUnset(now)) return now;", SHARED)
        self.assertIn('"HEAD / HTTP/1.0', NET)
        self.assertIn("!Shared.ClockUnset(t)", NET)  # a router that is itself unset is ignored
        back = (ROOT / "launcher/src/Backdrop.cs").read_text()
        self.assertNotIn("DateTime.Now", back); self.assertNotIn("DateTime.UtcNow", back)


if __name__ == "__main__":
    unittest.main()
