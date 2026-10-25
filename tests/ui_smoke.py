"""Run with dbus-run-session -- xvfb-run -a python3 tests/ui_smoke.py."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

os.environ["GDK_BACKEND"]="x11"
ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "tests/artifacts"
ARTIFACTS.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix="splate-ui-") as temp:
    env = dict(os.environ, XDG_CONFIG_HOME=temp, XDG_DATA_HOME=temp+"/data", XDG_RUNTIME_DIR=temp, WEBKIT_DISABLE_DMABUF_RENDERER="1", LIBGL_ALWAYS_SOFTWARE="1")
    report = Path(temp) / "report.json"
    with (ARTIFACTS / "ui-smoke.log").open("w") as log:
        p = subprocess.Popen([str(ROOT / "dist/linux-x64/SplateClient"), "--smoke-report", str(report)], env=env, stdout=log, stderr=log)
        try:
            for _ in range(100):
                if report.exists():
                    break
                if p.poll() is not None:
                    raise AssertionError("Window process exited; see ui-smoke.log")
                time.sleep(0.3)
            assert report.exists(), "No IPC round trip from WebKitGTK"
            data = json.loads(report.read_text())
            (ARTIFACTS / "ui-smoke.json").write_text(json.dumps(data, indent=2))
            import ctypes as c
            gtk=c.CDLL("libgtk-3.so.0");gdk=c.CDLL("libgdk-3.so.0");pix=c.CDLL("libgdk_pixbuf-2.0.so.0")
            gtk.gtk_init_check(None,None)
            gdk.gdk_get_default_root_window.restype=c.c_void_p
            root=gdk.gdk_get_default_root_window()
            gdk.gdk_window_get_width.argtypes=[c.c_void_p];gdk.gdk_window_get_height.argtypes=[c.c_void_p]
            gdk.gdk_pixbuf_get_from_window.argtypes=[c.c_void_p,c.c_int,c.c_int,c.c_int,c.c_int];gdk.gdk_pixbuf_get_from_window.restype=c.c_void_p
            image=gdk.gdk_pixbuf_get_from_window(root,0,0,gdk.gdk_window_get_width(root),gdk.gdk_window_get_height(root))
            pix.gdk_pixbuf_savev.argtypes=[c.c_void_p,c.c_char_p,c.c_char_p,c.c_void_p,c.c_void_p,c.c_void_p]
            assert pix.gdk_pixbuf_savev(image,os.fsencode(ARTIFACTS / "linux-ui.png"),b"png",None,None,None)
            (ARTIFACTS / "ui-smoke.json").write_text(json.dumps(data, indent=2))
            assert data["readyState"] == "complete" and data["hasApp"]
            assert data["themes"] == 12 and not data["horizontalOverflow"]
            startup = Path(temp) / "autostart/splatevpn.desktop"
            assert startup.exists() and "--minimized" in startup.read_text()
            data["autostartCreated"] = True
            # Repeated launches reach the running window rather than creating one.
            result = subprocess.run([str(ROOT / "dist/linux-x64/SplateClient")], env=env, capture_output=True, timeout=10)
            assert result.returncode == 0
            data["singleInstance"] = True
            (ARTIFACTS / "ui-smoke.json").write_text(json.dumps(data, indent=2))
            print("PASS native WebKitGTK window: IPC, 12 themes, no horizontal overflow, autostart, single instance; screenshot saved")
        finally:
            p.terminate()
            try:
                p.wait(timeout=5)
            except subprocess.TimeoutExpired:
                p.kill()
