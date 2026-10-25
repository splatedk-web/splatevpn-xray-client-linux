"""Real TUN test in two disposable namespaces. Does not touch host routing."""
import importlib.machinery
import json
import os
from pathlib import Path
import select
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
CLIENT, REMOTE = "splate-client-qa", "splate-remote-qa"
children = []


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def start(ns, *args, **kwargs):
    p = subprocess.Popen(["ip", "netns", "exec", ns, *args], **kwargs)
    children.append(p)
    return p


def python(ns, code):
    return run("ip", "netns", "exec", ns, "python3", "-c", code)


try:
    for ns in (CLIENT, REMOTE):
        run("ip", "netns", "add", ns)
        run("ip", "-n", ns, "link", "set", "lo", "up")
    run("ip", "link", "add", "splate-c0", "type", "veth", "peer", "name", "splate-r0")
    run("ip", "link", "set", "splate-c0", "netns", CLIENT)
    run("ip", "link", "set", "splate-r0", "netns", REMOTE)
    for ns, dev, v4, v6 in [(CLIENT, "splate-c0", "10.203.0.2/24", "fd00:203::2/64"), (REMOTE, "splate-r0", "10.203.0.1/24", "fd00:203::1/64")]:
        run("ip", "-n", ns, "addr", "add", v4, "dev", dev)
        run("ip", "-n", ns, "-6", "addr", "add", v6, "dev", dev, "nodad")
        run("ip", "-n", ns, "link", "set", dev, "up")
    run("ip", "-n", REMOTE, "addr", "add", "203.0.113.1/32", "dev", "lo")
    run("ip", "-n", REMOTE, "-6", "addr", "add", "2001:db8:203::1/128", "dev", "lo", "nodad")
    run("ip", "-n", CLIENT, "route", "add", "default", "via", "10.203.0.1")
    run("ip", "-n", CLIENT, "-6", "route", "add", "default", "via", "fd00:203::1")
    before = {f: run("ip", "-n", CLIENT, f, "route", "show", "table", "main") for f in ("-4", "-6")}
    run("ip", "netns", "exec", CLIENT, "sysctl", "-w", "net.ipv4.conf.all.rp_filter=1", "net.ipv4.conf.default.rp_filter=1", "net.ipv4.conf.splate-c0.rp_filter=1")
    echo = '''import socket,threading,time,sys
def serve(family,address):
 s=socket.socket(family);s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
 if family==socket.AF_INET6:s.setsockopt(socket.IPPROTO_IPV6,socket.IPV6_V6ONLY,1)
 s.bind((address,39080));s.listen()
 while True:
  c,_=s.accept();data=c.recv(1024);c.sendall(data);c.close()
threading.Thread(target=serve,args=(socket.AF_INET,'0.0.0.0'),daemon=True).start()
threading.Thread(target=serve,args=(socket.AF_INET6,'::'),daemon=True).start()
def udp(family,address):
 s=socket.socket(family,socket.SOCK_DGRAM)
 if family==socket.AF_INET6:s.setsockopt(socket.IPPROTO_IPV6,socket.IPV6_V6ONLY,1)
 s.bind((address,39083))
 while True:
  data,peer=s.recvfrom(2048);s.sendto(data,peer)
threading.Thread(target=udp,args=(socket.AF_INET,sys.argv[1]),daemon=True).start()
threading.Thread(target=udp,args=(socket.AF_INET6,sys.argv[2]),daemon=True).start()
time.sleep(300)
'''
    start(CLIENT, "python3", "-c", echo,"10.203.0.2","fd00:203::2")
    start(REMOTE, "python3", "-c", echo,"203.0.113.1","2001:db8:203::1")
    with tempfile.TemporaryDirectory(prefix="splate-network-") as temp:
        cfg = Path(temp) / "xray.json"
        cfg.write_text(json.dumps({"inbounds": [{"listen": "127.0.0.1", "port": 39081, "protocol": "socks", "settings": {"udp": True}}], "outbounds": [{"protocol": "freedom", "streamSettings": {"sockopt": {"interface": "splate-c0"}}}]}))
        start(CLIENT, str(ROOT / "core/xray"), "run", "-c", str(cfg), stdout=subprocess.DEVNULL)
        python(CLIENT, """import socket,time
for attempt in range(100):
 try:
  s=socket.create_connection(('127.0.0.1',39081),timeout=.1);s.close();break
 except OSError:time.sleep(.1)
else:raise AssertionError('SOCKS fixture did not become ready')
""")
        helper_code = f"import importlib.machinery; m=importlib.machinery.SourceFileLoader('network',{str(ROOT / 'scripts/splatevpn-network')!r}).load_module();m.CORE={str(ROOT / 'core/tun2socks')!r};m.main()"
        helper = start(CLIENT, "python3", "-u", "-c", helper_code, "39081", str(os.getpid()), stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
        if not select.select([helper.stdout], [], [], 10)[0] or helper.stdout.readline().strip() != "READY":
            raise AssertionError("TUN did not become ready")
        # Route lookup and data transfer both prove the tunnel is in the path.
        for family, address in [("-4", "203.0.113.1"), ("-6", "2001:db8:203::1")]:
            assert "SplateTUN" in run("ip", "-n", CLIENT, family, "route", "get", address)
        probe = '''import socket
s=socket.socket(FAMILY);s.settimeout(5)
BIND
s.connect((TARGET,39080));s.sendall(b'splate-incoming-check');assert s.recv(100)==b'splate-incoming-check'
'''
        for family, external, local in [("socket.AF_INET", "203.0.113.1", "10.203.0.2"), ("socket.AF_INET6", "2001:db8:203::1", "fd00:203::2")]:
            python(REMOTE, probe.replace("FAMILY", family).replace("BIND", f"s.bind(({external!r},0))").replace("TARGET", repr(local)))
            python(CLIENT, probe.replace("FAMILY", family).replace("BIND", "").replace("TARGET", repr(external)))
            udp_probe=probe.replace("socket.socket(FAMILY)","socket.socket(FAMILY,socket.SOCK_DGRAM)").replace("39080","39083")
            python(CLIENT, udp_probe.replace("FAMILY",family).replace("BIND","").replace("TARGET",repr(external)))
            python(REMOTE, udp_probe.replace("FAMILY",family).replace("BIND",f"s.bind(({external!r},0))").replace("TARGET",repr(local)))
        for family in ("-4", "-6"):
            assert [line.strip() for line in before[family].splitlines()] == [line.strip() for line in run("ip", "-n", CLIENT, family, "route", "show", "table", "main").splitlines() if "SplateTUN" not in line]
        run("ip", "-n", CLIENT, "addr", "add", "10.203.0.3/24", "dev", "splate-c0")
        time.sleep(1.5)
        python(REMOTE, probe.replace("FAMILY", "socket.AF_INET").replace("BIND", "s.bind(('203.0.113.1',0))").replace("TARGET", repr("10.203.0.3")))
        helper.stdin.close()
        assert helper.wait(timeout=8) == 0
        assert "SplateTUN" not in run("ip", "-n", CLIENT, "link", "show")
        for family in ("-4", "-6"):
            assert "20214" not in run("ip", "-n", CLIENT, family, "rule", "show")
        owner = start(CLIENT, "python3", "-c", "import time;time.sleep(60)")
        helper2 = start(CLIENT, "python3", "-u", "-c", helper_code, "39081", str(owner.pid), stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
        assert select.select([helper2.stdout], [], [], 10)[0] and helper2.stdout.readline().strip() == "READY"
        owner.terminate()
        owner.wait(timeout=3)
        assert helper2.wait(timeout=8) == 0
        assert "SplateTUN" not in run("ip", "-n", CLIENT, "link", "show")
        print("PASS real TUN IPv4/IPv6 TCP/UDP: outgoing SOCKS, unsolicited incoming, strict rp_filter, changed address, unchanged main routes, EOF and parent-death cleanup")
        # Full model/core/proxy session in the same isolated network. Two VLESS
        # fixtures allow a deterministic real outage, independent of the internet.
        http = '''from http.server import BaseHTTPRequestHandler,HTTPServer
class Handler(BaseHTTPRequestHandler):
 def do_GET(self):self.send_response(204);self.end_headers()
 def log_message(self,*args):pass
HTTPServer(('0.0.0.0',39082),Handler).serve_forever()
'''
        start(REMOTE,"python3","-c",http)
        servers=[]
        for port in (39091,39092):
            path=Path(temp)/f"server-{port}.json"
            path.write_text(json.dumps({"log":{"loglevel":"warning"},"inbounds":[{"listen":"0.0.0.0","port":port,"protocol":"vless","settings":{"decryption":"none","clients":[{"id":"00000000-0000-0000-0000-000000000001"}]}}],"outbounds":[{"protocol":"freedom","settings":{"finalRules":[{"action":"allow","ip":["203.0.113.1/32"]}]}}]}))
            servers.append(start(REMOTE,str(ROOT/"core/xray"),"run","-c",str(path),stdout=subprocess.DEVNULL))
        time.sleep(.5)
        env=dict(os.environ,XDG_CONFIG_HOME=temp+"/config",XDG_DATA_HOME=temp+"/data",GSETTINGS_BACKEND="keyfile")
        session=start(CLIENT,str(ROOT/"dist/checks/PortChecks"),"--tun-session",str(ROOT),str(servers[0].pid),env=env)
        assert session.wait(timeout=120)==0,"Full TUN session failed"
        assert "SplateTUN" not in run("ip","-n",CLIENT,"link","show")
        for family in ("-4","-6"):
            assert "20214" not in run("ip","-n",CLIENT,family,"rule","show")
finally:
    for child in reversed(children):
        if child.poll() is None:
            child.terminate()
            try:
                child.wait(timeout=3)
            except subprocess.TimeoutExpired:
                child.kill()
    for ns in (CLIENT, REMOTE):
        subprocess.run(["ip", "netns", "del", ns], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
