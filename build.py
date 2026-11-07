"""
Builds dist/LED Control/LED Control.exe with PyInstaller.

    python build.py
"""

import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def main():
    if not (HERE / "assets" / "icon.ico").exists():
        subprocess.check_call([sys.executable, str(HERE / "make_icon.py")])
    sep = ";"  # PyInstaller --add-data separator on Windows
    cmd = [
        sys.executable, "-m", "PyInstaller",
        "--noconfirm", "--clean", "--windowed",
        "--name", "LED Control",
        "--icon", str(HERE / "assets" / "icon.ico"),
        "--version-file", str(HERE / "version_info.txt"),
        "--add-data", f"{HERE / 'assets'}{sep}assets",
        "--distpath", str(HERE / "dist"),
        "--workpath", str(HERE / "build"),
        "--specpath", str(HERE / "build"),
        # soundcard (music sync) loads C headers from its package folder at runtime.
        "--collect-data", "soundcard",
        # Modules the app never uses. scipy/PIL/colorthief only power Fluent Widgets'
        # acrylic blur, which it imports optionally; leaving them out saves ~70 MB.
        # (numpy stays: music sync uses it.)
        *[a for m in ("PySide6.QtWebEngineCore", "PySide6.QtWebEngineWidgets", "PySide6.QtQml",
                      "PySide6.QtQuick", "PySide6.Qt3DCore", "PySide6.QtMultimedia",
                      "PySide6.QtCharts", "PySide6.QtDataVisualization", "PySide6.QtPdf",
                      "scipy", "PIL", "colorthief", "tkinter", "unittest")
          for a in ("--exclude-module", m)],
        str(HERE / "led_desktop.py"),
    ]
    subprocess.check_call(cmd, cwd=HERE)
    prune(HERE / "dist" / "LED Control" / "_internal" / "PySide6")
    print("\nBuilt:", HERE / "dist" / "LED Control" / "LED Control.exe")


def prune(qt):
    """Remove Qt files a widgets-only app never loads (Quick/QML/PDF, software OpenGL, translations)."""
    import shutil
    patterns = ["opengl32sw.dll", "Qt6Quick*.dll", "Qt6Qml*.dll", "Qt6Pdf*.dll",
                "Qt6VirtualKeyboard*.dll", "plugins/imageformats/qpdf.dll",
                "plugins/platforminputcontexts", "translations"]
    for pat in patterns:
        for p in qt.glob(pat):
            shutil.rmtree(p) if p.is_dir() else p.unlink()


if __name__ == "__main__":
    main()
