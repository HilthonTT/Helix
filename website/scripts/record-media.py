"""Record the documentation GIFs and screenshots from the real Helix app.

Windows only. Publishes Helix under a separate company and title so its data
lives in %LOCALAPPDATA%\\HelixMedia rather than beside a real install, registers
a throwaway account, adds drives on RFC 5737 documentation addresses (they reach
nothing, on any network), and drives the window through UI Automation while
PrintWindow captures frames. Stop any running Helix first: the single-instance
mutex would hand the launch to it.

    python -m venv .venv && .venv\\Scripts\\pip install pywinauto pillow
    .venv\\Scripts\\python website\\scripts\\record-media.py

A few steps click with the real mouse (the drive rows' chips are tap targets
with no UI Automation pattern), so leave the machine alone while it runs.
"""

import ctypes
import ctypes.wintypes as wt
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

import pywinauto.mouse as mouse
from PIL import Image, ImageStat
from pywinauto import Application

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "website" / "public" / "media"
APP_DIR = Path(tempfile.gettempdir()) / "helix-media-app"
DATA_DIR = Path(os.environ["LOCALAPPDATA"]) / "HelixMedia"

USERNAME = "demo"
PASSWORD = "Demo-Helix-2026!"
DRIVES = [
    ("Media Vault", "192.0.2.20"),
    ("Backups", "192.0.2.20"),
    ("Photo Archive", "192.0.2.20"),
    ("Documents", "198.51.100.21"),
    ("Music Library", "198.51.100.21"),
    ("Downloads", "198.51.100.21"),
]

GIF_WIDTH = 1100
PNG_WIDTH = 1600

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
user32.SetProcessDPIAware()


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
        ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD),
    ]


def publish():
    shutil.rmtree(APP_DIR, ignore_errors=True)
    subprocess.run(
        [
            "dotnet", "publish", str(ROOT / "src" / "Helix.App" / "Helix.App.csproj"),
            "-c", "Release", "-f", "net10.0-windows10.0.19041.0",
            "-p:RuntimeIdentifierOverride=win-x64",
            "-p:Company=HelixMedia", "-p:AssemblyTitle=HelixMedia",
            "-o", str(APP_DIR), "-v", "q", "-nologo",
        ],
        check=True,
    )


def launch():
    shutil.rmtree(DATA_DIR, ignore_errors=True)
    exe = APP_DIR / "Helix.App.exe"
    subprocess.Popen([str(exe)], cwd=str(APP_DIR))
    time.sleep(8)


def win():
    return Application(backend="uia").connect(path="Helix.App.exe", timeout=20).top_window()


def grab(hwnd):
    r = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top
    hdc = user32.GetWindowDC(hwnd)
    mdc = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(mdc, bmp)
    user32.PrintWindow(hwnd, mdc, 2)
    bi = BITMAPINFOHEADER(biSize=ctypes.sizeof(BITMAPINFOHEADER), biWidth=w, biHeight=-h, biPlanes=1, biBitCount=32)
    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mdc)
    user32.ReleaseDC(hwnd, hdc)
    frame = Image.frombuffer("RGB", (w, h), buf, "raw", "BGRX", 0, 1)
    # Drop the resize border and the title bar.
    return frame.crop((11, 48, frame.width - 11, frame.height - 11))


def find(name=None, ctype=None, idx=0, timeout=10):
    end = time.time() + timeout
    while True:
        hits = [
            c for c in win().descendants()
            if (ctype is None or c.element_info.control_type == ctype)
            and (name is None or c.element_info.name == name)
            and c.element_info.rectangle.width()
        ]
        if len(hits) > idx:
            return hits[idx]
        if time.time() > end:
            raise LookupError(f"{ctype} {name!r}")
        time.sleep(0.3)


def invoke(control):
    try:
        control.invoke()
    except Exception:
        control.click_input()


def type_into(control, text, delay=0.06):
    control.set_focus()
    for i in range(1, len(text) + 1):
        control.iface_value.SetValue(text[:i])
        if delay:
            time.sleep(delay)


class Recorder:
    def __init__(self, fps=8):
        self.fps = fps
        self.frames = []
        self._stop = False

    def __enter__(self):
        self.hwnd = win().handle
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()
        return self

    def _run(self):
        while not self._stop:
            started = time.time()
            self.frames.append(grab(self.hwnd))
            time.sleep(max(0, 1 / self.fps - (time.time() - started)))

    def __exit__(self, *exc):
        self._stop = True
        self.thread.join()


def deblank(frames, box):
    """Replaces frames whose list area is empty mid-relayout with the one before."""
    out = []
    for f in frames:
        if out and ImageStat.Stat(f.crop(box).convert("L")).stddev[0] < 6:
            out.append(out[-1])
        else:
            out.append(f)
    return out


def save_gif(frames, name, fps=8, hold_last=1.5):
    scaled = [f.resize((GIF_WIDTH, round(f.height * GIF_WIDTH / f.width)), Image.LANCZOS) for f in frames]
    step = int(1000 / fps)
    seq, durations = [], []
    for f in scaled:
        if seq and f.tobytes() == seq[-1].tobytes():
            durations[-1] += step
        else:
            seq.append(f)
            durations.append(step)
    durations[-1] += int(hold_last * 1000)
    pal = [f.convert("P", palette=Image.ADAPTIVE, colors=255, dither=Image.NONE) for f in seq]
    pal[0].save(OUT / name, save_all=True, append_images=pal[1:], duration=durations, loop=0, optimize=True, disposal=1)


def save_png(frame, name):
    frame.resize((PNG_WIDTH, round(frame.height * PNG_WIDTH / frame.width)), Image.LANCZOS).save(OUT / name, optimize=True)


def snap(name):
    save_png(grab(win().handle), name)


def add_drive(name, host, delay=0.0):
    invoke(find("Add Drive", "Button"))
    time.sleep(1.5)
    fields = [
        ("Add your drive's name", name),
        ("IP address or hostname", host),
        ("Add your NAS' username", "admin"),
        ("Add your NAS' password", "demo-password"),
    ]
    for label, value in fields:
        type_into(find(label, "Edit"), value, delay)
        time.sleep(0.4 if delay else 0.2)
    time.sleep(1.0 if delay else 0.5)
    invoke(find("Save", "Button"))
    time.sleep(2.5)


def click_row_checkbox(letter):
    r = find(letter, "Text").rectangle()
    mouse.click(coords=(r.left - 55, (r.top + r.bottom) // 2))


def register():
    invoke(find("Don't have an account?", "Button"))
    time.sleep(1.5)
    find("Username", "Edit").iface_value.SetValue(USERNAME)
    find("Password", "Edit").iface_value.SetValue(PASSWORD)
    find("Confirm Password", "Edit").iface_value.SetValue(PASSWORD)
    invoke(find("Register", "Button"))
    time.sleep(4)
    invoke(find("I've saved it", "Button"))
    time.sleep(5)


def create_group(name, indexes):
    type_into(find("Home, Office, Backup run…", "Edit"), name, 0.03)
    boxes = [c for c in win().descendants() if c.element_info.control_type == "CheckBox" and c.element_info.rectangle.width()]
    for i in indexes:
        boxes[i].click_input()
        time.sleep(0.3)
    invoke(find("Save", "Button"))
    time.sleep(2)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    if "--no-publish" not in sys.argv:
        publish()
    launch()
    win().set_focus()

    register()
    for name, host in DRIVES:
        add_drive(name, host)

    with Recorder() as r:
        time.sleep(1)
        add_drive("Projects", "203.0.113.10", delay=0.07)
        time.sleep(2)
    save_gif(r.frames, "add-drive.gif")

    with Recorder() as r:
        time.sleep(1)
        find("", "Text").click_input()
        time.sleep(9)
    save_gif(r.frames, "diagnose.gif", hold_last=2.5)
    save_png(r.frames[-1], "diagnose.png")
    invoke(find("Close", "Button", idx=1))
    time.sleep(1.5)

    search = find("Search drives", "Edit")
    with Recorder() as r:
        time.sleep(1)
        type_into(search, "doc", 0.3)
        time.sleep(1.5)
        for i in range(2, -1, -1):
            search.iface_value.SetValue("doc"[:i])
            time.sleep(0.15)
        time.sleep(1.2)
        for header in ("Name", "Name", "Letter"):
            find(header, "Text").click_input()
            time.sleep(1.5)
    w, h = r.frames[0].size
    save_gif(deblank(r.frames, (int(w * 0.21), int(h * 0.57), int(w * 0.6), int(h * 0.9))), "search.gif")

    with Recorder() as r:
        time.sleep(1)
        for letter in ("U", "V", "W"):
            click_row_checkbox(letter)
            time.sleep(1.3)
        time.sleep(2)
    save_gif(r.frames, "selection.gif")
    find("Clear", "Text").click_input()
    time.sleep(1)

    find("Groups", "Pane").click_input()
    time.sleep(1.5)
    create_group("Office", [3, 4, 5])
    create_group("Media", [0, 1, 2])
    snap("groups.png")
    invoke(find("Close", "Button", idx=1))
    time.sleep(1)

    find("Auditlogs", "RadioButton").click_input()
    time.sleep(3)
    snap("audit-log.png")

    find("Settings", "RadioButton").click_input()
    time.sleep(3)
    find("Preferences", "RadioButton").click_input()
    time.sleep(2)
    snap("settings.png")
    r = find("Theme", "Text").rectangle()
    mouse.scroll(coords=(r.left + 400, 700), wheel_dist=-6)
    time.sleep(1.5)
    with Recorder() as rec:
        time.sleep(1.2)
        for theme in ("Light", "Midnight", "Amethyst", "Sand", "Glacier", "Dark", "Crimson"):
            find(theme, "Pane").click_input()
            time.sleep(1.6)
        time.sleep(0.5)
    save_gif(rec.frames, "themes.gif", hold_last=1.0)

    find("Dashboard", "RadioButton").click_input()
    time.sleep(3)
    snap("dashboard.png")
    find("Logout", "Text").click_input()
    time.sleep(3)
    snap("sign-in.png")

    with Recorder() as r:
        time.sleep(1.2)
        type_into(find("Username", "Edit"), USERNAME, 0.12)
        time.sleep(0.3)
        type_into(find("Password", "Edit"), PASSWORD, 0.05)
        time.sleep(0.6)
        invoke(find("Login", "Button"))
        time.sleep(5)
        for page, wait in (("Auditlogs", 2.8), ("Settings", 2.5), ("Dashboard", 2.5)):
            find(page, "RadioButton").click_input()
            time.sleep(wait)
    save_gif(r.frames, "overview.gif", hold_last=1.0)

    subprocess.run(["taskkill", "/IM", "Helix.App.exe", "/F"], capture_output=True)
    time.sleep(2)
    shutil.rmtree(DATA_DIR, ignore_errors=True)


if __name__ == "__main__":
    main()
