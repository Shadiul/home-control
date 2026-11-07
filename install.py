"""
Installs the built app for the current user (no admin needed):

  - copies dist/LED Control  ->  %LOCALAPPDATA%\\Programs\\LED Control
  - adds a Start menu shortcut
  - lists it in Settings > Apps (with Uninstall)

    python install.py            install / update
    python install.py --remove   uninstall (settings in %APPDATA%\\LED Control are kept)
"""

import os
import shutil
import subprocess
import sys
import winreg
from pathlib import Path

HERE = Path(__file__).resolve().parent
NAME = "LED Control"
VERSION = "1.1.0"
SRC = HERE / "dist" / NAME
TARGET = Path(os.environ["LOCALAPPDATA"]) / "Programs" / NAME
EXE = TARGET / f"{NAME}.exe"
SHORTCUT = Path(os.environ["APPDATA"]) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / f"{NAME}.lnk"
UNINSTALL_KEY = rf"Software\Microsoft\Windows\CurrentVersion\Uninstall\{NAME}"
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"


def stop_running():
    """Close the app and wait until Windows has actually released its files."""
    import time
    subprocess.run(["taskkill", "/IM", f"{NAME}.exe", "/F"], capture_output=True)
    for _ in range(50):
        out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {NAME}.exe", "/NH"],
                             capture_output=True, text=True).stdout
        if f"{NAME}.exe" not in out:
            break
        time.sleep(0.2)
    time.sleep(0.5)


def remove_tree(path):
    """rmtree with a few retries: antivirus or a just-closed process can hold files briefly."""
    import time
    for attempt in range(10):
        try:
            shutil.rmtree(path)
            return
        except FileNotFoundError:
            return
        except PermissionError:
            if attempt == 9:
                raise
            time.sleep(0.5)


def make_shortcut():
    ps = (
        "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:LNK);"
        "$s.TargetPath = $env:EXE; $s.WorkingDirectory = $env:DIR;"
        "$s.IconLocation = $env:EXE + ',0'; $s.Description = 'Control your Tuya light'; $s.Save()"
    )
    env = dict(os.environ, LNK=str(SHORTCUT), EXE=str(EXE), DIR=str(TARGET))
    subprocess.run(["powershell", "-NoProfile", "-Command", ps], check=True, env=env)


def register():
    size_kb = sum(f.stat().st_size for f in TARGET.rglob("*") if f.is_file()) // 1024
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as k:
        for name, value in {
            "DisplayName": NAME, "DisplayVersion": VERSION, "Publisher": "Personal",
            "DisplayIcon": f"{EXE},0", "InstallLocation": str(TARGET),
            "UninstallString": f'"{EXE}" --uninstall',
        }.items():
            winreg.SetValueEx(k, name, 0, winreg.REG_SZ, value)
        winreg.SetValueEx(k, "EstimatedSize", 0, winreg.REG_DWORD, size_kb)
        winreg.SetValueEx(k, "NoModify", 0, winreg.REG_DWORD, 1)
        winreg.SetValueEx(k, "NoRepair", 0, winreg.REG_DWORD, 1)


def install():
    if not (SRC / f"{NAME}.exe").exists():
        sys.exit("Build first: python build.py")
    stop_running()
    if TARGET.exists():
        remove_tree(TARGET)
    shutil.copytree(SRC, TARGET)
    SHORTCUT.parent.mkdir(parents=True, exist_ok=True)
    make_shortcut()
    register()
    print(f"Installed to {TARGET}\nStart menu: {SHORTCUT}")


def remove():
    stop_running()
    SHORTCUT.unlink(missing_ok=True)
    for key, value in ((UNINSTALL_KEY, None), (RUN_KEY, NAME)):
        try:
            if value is None:
                winreg.DeleteKey(winreg.HKEY_CURRENT_USER, key)
            else:
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key, 0, winreg.KEY_SET_VALUE) as k:
                    winreg.DeleteValue(k, value)
        except FileNotFoundError:
            pass
    if TARGET.exists():
        remove_tree(TARGET)
    print("Removed. Your settings are still in %APPDATA%\\LED Control.")


if __name__ == "__main__":
    remove() if "--remove" in sys.argv else install()
