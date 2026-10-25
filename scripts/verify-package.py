import hashlib
import json
from pathlib import Path
import subprocess
import tarfile

root = Path(__file__).resolve().parents[1]
package = root / "dist/splatevpn_2.14.0-1_amd64.deb"
expected = {"opt/splatevpn/core/" + entry["file"]: entry["sha256"] for entry in json.loads((root / "docs/linux-core-manifest.json").read_text())}
required = {
    "opt/splatevpn/SplateClient", "opt/splatevpn/Photino.Native.so",
    "opt/splatevpn/core/xray", "opt/splatevpn/core/xray.bin", "opt/splatevpn/core/tun2socks",
    "opt/splatevpn/Assets/presets/preset_white_cidr.txt",
    "opt/splatevpn/wwwroot/js/linux-bridge.js", "usr/libexec/splatevpn-network",
    "usr/share/polkit-1/actions/io.splatevpn.network.policy", "usr/share/applications/splatevpn.desktop",
}
seen = set()
with subprocess.Popen(["dpkg-deb", "--fsys-tarfile", str(package)], stdout=subprocess.PIPE) as p:
    with tarfile.open(fileobj=p.stdout, mode="r|") as archive:
        for member in archive:
            name = member.name.removeprefix("./")
            seen.add(name)
            assert member.uid == 0 and member.gid == 0, name + " is not root-owned"
            assert not member.mode & 0o022, name + " is writable by non-root"
            assert ".." not in Path(name).parts and not name.endswith((".exe", "wintun.dll")), name
            if name in expected:
                assert hashlib.sha256(archive.extractfile(member).read()).hexdigest() == expected[name], name + " hash mismatch"
            if name in ("usr/libexec/splatevpn-network", "opt/splatevpn/core/xray"):
                assert member.mode == 0o755 and b"\r" not in archive.extractfile(member).readline()
    assert p.wait() == 0
assert required <= seen, "Missing: " + repr(required - seen)
print("PASS Debian package: root ownership, safe permissions, required runtime/helper/UI, Linux-only cores, SHA-256, Unix shebangs")
