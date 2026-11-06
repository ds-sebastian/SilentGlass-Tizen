"""Offline execution tests: private chains only, both families, repair, inverse and honest failures."""
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / 'devices/s90d/device/internet/firewall.sh'
HOST = ROOT / 'devices/s90d/pi/internet_controller.py'
STUB = r'''#!PYTHON
import json, os, sys
from pathlib import Path
p=Path(os.environ['FW_DB']); all=json.loads(p.read_text()); proto=Path(sys.argv[0]).name
s=all[proto]; a=sys.argv[1:]
if a[:1]==['-w']: a=a[2:]
op, chain=a[:2]; rule=a[2:]
if os.environ.get('FAIL_V6') and proto=='ip6tables': sys.exit(4)
if op=='-S':
 if chain not in s: sys.exit(1)
 print(('-P '+chain+' ACCEPT') if chain in ('INPUT','OUTPUT','FORWARD') else '-N '+chain)
 for r in s[chain]: print('-A '+chain+' '+' '.join(r))
 sys.exit(0)
if op=='-N':
 if chain in s: sys.exit(1)
 s[chain]=[]
elif chain not in s: sys.exit(1)
elif op=='-C': sys.exit(0 if rule in s[chain] else 1)
elif op=='-A': s[chain].append(rule)
elif op=='-I': s[chain].insert(int(rule[0])-1,rule[1:])
elif op=='-D':
 if rule not in s[chain]: sys.exit(1)
 s[chain].remove(rule)
elif op=='-F': s[chain]=[]
elif op=='-X':
 if s[chain]: sys.exit(1)
 del s[chain]
else: sys.exit(2)
p.write_text(json.dumps(all))
'''.replace('PYTHON', sys.executable)


class FirewallTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.data = self.root / 'opt/usr/home/owner/apps_rw/com.samsung.tv.bixby-promotion/data'
        self.data.mkdir(parents=True)
        self.bin = self.root / 'bin'; self.bin.mkdir()
        for name in ('iptables','ip6tables'):
            f=self.bin/name; f.write_text(STUB); f.chmod(0o755)
        for name in ('chown','chsmack'):
            f=self.bin/name; f.write_text('#!/bin/sh\nexit 0\n'); f.chmod(0o755)
        self.db=self.root/'rules.json'
        base={'INPUT':[['-j','STC_IN']], 'OUTPUT':[['-j','STC_OUT']], 'FORWARD':[['-j','OTBR_FORWARD_CHAIN']], 'STC_IN':[], 'STC_OUT':[], 'OTBR_FORWARD_CHAIN':[['-j','DROP']]}
        self.original={t:base for t in ('iptables','ip6tables')}
        self.db.write_text(json.dumps(self.original))
        self.env=dict(os.environ, SILENTGLASS_TEST_ROOT=str(self.root), FW_DB=str(self.db), PATH=str(self.bin)+os.pathsep+os.environ['PATH'])

    def tearDown(self): self.tmp.cleanup()

    def run_fw(self,*args,fail=False):
        p=subprocess.run(['sh','-s','--',*args],input=SCRIPT.read_text(),env=self.env,text=True,capture_output=True)
        self.assertEqual(p.returncode != 0, fail, p.stdout+p.stderr)
        return p

    def test_roundtrip_and_repeat_preserve_vendor_and_forward_rules(self):
        self.run_fw()
        self.assertEqual(json.loads(self.db.read_text()), self.original)
        (self.data/'internet-off').touch()
        self.run_fw(); once=json.loads(self.db.read_text()); self.run_fw()
        self.assertEqual(once,json.loads(self.db.read_text()))
        for t,s in once.items():
            self.assertEqual(s['OUTPUT'][0], ['-j','SG_WAN_OUT'])
            self.assertEqual(s['INPUT'][0], ['-j','SG_WAN_IN'])
            self.assertEqual(s['FORWARD'],self.original[t]['FORWARD'])
            self.assertEqual(s['SG_WAN_OUT'][-2:], [['-p','tcp','-j','REJECT','--reject-with','tcp-reset'],['-j','REJECT']])
        (self.data/'internet-off').unlink(); self.run_fw()
        self.assertEqual(json.loads(self.db.read_text()),self.original)
        self.assertIn('internet=on', (self.data/'internet-state.txt').read_text())

    def test_repair_and_emergency_inverse(self):
        (self.data/'internet-off').touch(); self.run_fw()
        r=json.loads(self.db.read_text()); r['iptables']['SG_WAN_OUT'].pop(); r['ip6tables']['OUTPUT'].reverse(); self.db.write_text(json.dumps(r))
        self.run_fw()
        s=json.loads(self.db.read_text()); self.assertEqual(s['iptables']['SG_WAN_OUT'][-1],['-j','REJECT'])
        self.assertEqual(s['ip6tables']['OUTPUT'][0],['-j','SG_WAN_OUT'])
        self.run_fw('allow'); self.assertEqual(json.loads(self.db.read_text()),self.original)

    def test_ipv6_failure_never_reports_blocked(self):
        (self.data/'internet-off').touch(); self.env['FAIL_V6']='1'; self.run_fw(fail=True)
        self.assertIn('internet=error',(self.data/'internet-state.txt').read_text())

    def test_lan_allowed_wan_rejected(self):
        (self.data/'internet-off').touch(); self.run_fw(); r=json.loads(self.db.read_text())
        for addr in ('127.0.0.1','192.168.1.10','172.16.0.4','10.1.2.3','169.254.1.2','224.0.0.251','fd00::1','fe80::1','ff02::1','::1','1.1.1.1','8.8.8.8','2606:4700:4700::1111'):
            ip=ipaddress.ip_address(addr); t='iptables' if ip.version==4 else 'ip6tables'
            nets=[x[x.index('-d')+1] for x in r[t]['SG_WAN_OUT'] if '-d' in x]
            allowed=any(ip in ipaddress.ip_network(n) for n in nets)
            self.assertEqual(allowed,ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_multicast)


class ControllerTests(unittest.TestCase):
    def setUp(self):
        spec=importlib.util.spec_from_file_location('netctl',HOST)
        assert spec is not None and spec.loader is not None
        self.m=importlib.util.module_from_spec(spec); spec.loader.exec_module(self.m)

    def test_helper_cli(self):
        c=self.m.command('tv-access','192.168.1.50','echo test')
        self.assertIn('--tv',c); self.assertNotIn('--ip',c)

    def test_messages_are_only_wakeups_never_commands(self):
        c=self.m.Controller({'base':'test'})
        for msg in ('off',';sh','a'*100,'00000000-0000-0000-0000-000000000000|off;sh'):
            c.message('test/internet_request',msg); self.assertFalse(c.wake.is_set())
        c.message('test/internet_request','00000000-0000-0000-0000-000000000000|off'); self.assertTrue(c.wake.is_set())
        c.wake.clear(); c.message('test/internet_request',c.seen); self.assertFalse(c.wake.is_set())
