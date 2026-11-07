"""
LED Control - Windows 11 desktop app for a Tuya Wi-Fi light.

Fluent design (Mica, light/dark theme, accent colour), tray icon with quick controls,
single instance, optional start with Windows. LED logic is shared with the browser panel
through led_core.py.

Run:  pythonw led_desktop.py   [--minimized]
"""

import ctypes
import os
import sys
import threading
import time
from pathlib import Path

from PySide6.QtCore import QObject, QPointF, QRectF, QSize, Qt, QTimer, Signal
from PySide6.QtGui import QColor, QIcon, QPainter, QPen, QRadialGradient
from PySide6.QtNetwork import QLocalServer, QLocalSocket
from PySide6.QtWidgets import (QAbstractButton, QApplication, QFormLayout, QGridLayout,
                               QHBoxLayout, QStackedWidget, QSystemTrayIcon, QVBoxLayout,
                               QWidget)
from qfluentwidgets import (Action, BodyLabel, CaptionLabel, CardWidget, ColorDialog, ComboBox,
                            FlowLayout, FluentIcon as FIF, FluentWindow, IconWidget, InfoBar,
                            InfoBarPosition, LineEdit, MessageBox, NavigationItemPosition,
                            PasswordLineEdit, PrimaryPushButton, PushButton, PushSettingCard,
                            RoundMenu, ScrollArea, SegmentedWidget, SettingCard, SettingCardGroup,
                            SimpleCardWidget, Slider, StrongBodyLabel, SubtitleLabel, SwitchButton,
                            SystemThemeListener, SystemTrayMenu, Theme, TitleLabel, ToolTipFilter,
                            TransparentToolButton, isDarkTheme, qconfig, setTheme, setThemeColor)

import led_core
import led_music
from led_core import APP_NAME, APP_VERSION, CONFIG_DIR, Controller

APP_ID = "LEDControl.Desktop"
INSTANCE_KEY = f"LEDControl-{os.environ.get('USERNAME', 'user')}"
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
POLL_MS = 15000

SCENES = [
    # name, icon, command
    ("Bright", FIF.BRIGHTNESS, {"action": "white", "brightness": 100, "temp": 70}),
    ("Reading", FIF.BOOK_SHELF, {"action": "white", "brightness": 80, "temp": 45}),
    ("Relax", FIF.CAFE, {"action": "white", "brightness": 35, "temp": 0}),
    ("Night", FIF.QUIET_HOURS, {"action": "colour", "h": 25, "s": 95, "v": 4}),
    ("Movie", FIF.MOVIE, {"action": "colour", "h": 230, "s": 85, "v": 15}),
    ("Party", FIF.CALORIES, {"action": "colour", "h": 300, "s": 90, "v": 100}),
]
SWATCHES = [(0, 100), (16, 100), (32, 100), (50, 100), (90, 90), (130, 100),
            (175, 90), (200, 100), (225, 100), (265, 85), (290, 80), (325, 80)]


def resource(name):
    base = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return str(base / "assets" / name)


def wrapped(label):
    """Let a label wrap instead of forcing its page wider than the window."""
    label.setWordWrap(True)
    label.setMinimumWidth(0)
    return label


def temp_to_color(t):
    """0 = warm .. 100 = cool, as a display colour."""
    warm, mid, cool = (255, 157, 59), (255, 226, 184), (219, 232, 255)
    a, b, k = (warm, mid, t / 50) if t < 50 else (mid, cool, (t - 50) / 50)
    return QColor(*[round(a[i] + (b[i] - a[i]) * k) for i in range(3)])


def warmth_label(t):
    return "Warm" if t < 20 else "Soft" if t < 42 else "Neutral" if t <= 58 else "Daylight" if t < 80 else "Cool"


# ---------------------------------------------------------------- background worker

class Worker(QObject):
    """Runs LED calls one at a time off the UI thread. Jobs with coalesce=True replace any
    queued job with the same tag, so dragging a slider only sends the latest value."""

    finished = Signal(str, object, object)  # tag, result, error text

    def __init__(self):
        super().__init__()
        self._jobs = []
        self._cv = threading.Condition()
        self._stop = False
        threading.Thread(target=self._run, daemon=True).start()

    def submit(self, tag, fn, coalesce=False):
        with self._cv:
            if coalesce:
                self._jobs = [j for j in self._jobs if j[0] != tag]
            self._jobs.append((tag, fn))
            self._cv.notify()

    def pending(self, tag):
        with self._cv:
            return any(j[0] == tag for j in self._jobs)

    def stop(self):
        with self._cv:
            self._stop = True
            self._cv.notify()

    def _run(self):
        while True:
            with self._cv:
                while not self._jobs and not self._stop:
                    self._cv.wait()
                if self._stop:
                    return
                tag, fn = self._jobs.pop(0)
            try:
                result, err = fn(), None
            except Exception as e:  # tinytuya raises a mix of exception types
                result, err = None, str(e) or e.__class__.__name__
            self.finished.emit(tag, result, err)


# ---------------------------------------------------------------- small custom widgets

class LightPreview(QWidget):
    """A glowing bulb that shows the light's current colour and brightness. Click to toggle."""

    clicked = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(176, 176)
        self.setCursor(Qt.PointingHandCursor)
        self.setToolTip("Turn on or off")
        self.installEventFilter(ToolTipFilter(self))
        self._on, self._color, self._level = False, QColor(255, 200, 120), 1.0
        qconfig.themeChanged.connect(self.update)

    def setLight(self, on, color, level):
        self._on, self._color, self._level = on, color, max(0.0, min(1.0, level))
        self.update()

    def mouseReleaseEvent(self, e):
        if e.button() == Qt.LeftButton and self.rect().contains(e.position().toPoint()):
            self.clicked.emit()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        c = QPointF(self.width() / 2, self.height() / 2)
        r_out, r_in = self.width() / 2, self.width() * 0.32
        if self._on:
            strength = 0.35 + 0.65 * self._level
            glow = QRadialGradient(c, r_out)
            col = QColor(self._color)
            col.setAlphaF(0.55 * strength)
            glow.setColorAt(0.45, col)
            col.setAlphaF(0.0)
            glow.setColorAt(1.0, col)
            p.setPen(Qt.NoPen)
            p.setBrush(glow)
            p.drawEllipse(c, r_out, r_out)
            body = QRadialGradient(QPointF(c.x(), c.y() - r_in * 0.3), r_in * 1.2)
            body.setColorAt(0.0, QColor(255, 255, 255))
            core = QColor(self._color)
            core.setAlphaF(0.55 + 0.45 * strength)
            body.setColorAt(0.55, core)
            body.setColorAt(1.0, self._color.darker(115))
            p.setBrush(body)
        else:
            dark = isDarkTheme()
            body = QRadialGradient(QPointF(c.x(), c.y() - r_in * 0.3), r_in * 1.2)
            body.setColorAt(0.0, QColor(90, 90, 90) if dark else QColor(250, 250, 250))
            body.setColorAt(1.0, QColor(45, 45, 45) if dark else QColor(214, 214, 214))
            p.setBrush(body)
            p.setPen(QPen(QColor(255, 255, 255, 25) if dark else QColor(0, 0, 0, 25), 1))
        p.drawEllipse(c, r_in, r_in)


class Swatch(QAbstractButton):
    def __init__(self, h, s, parent=None):
        super().__init__(parent)
        self.h, self.s = h, s
        self.color = QColor.fromHsvF(h / 360, s / 100, 1.0)
        self.setFixedSize(36, 36)
        self.setCursor(Qt.PointingHandCursor)
        self.setCheckable(True)
        self.setAutoExclusive(True)

    def enterEvent(self, e):
        self.update()
        super().enterEvent(e)

    def leaveEvent(self, e):
        self.update()
        super().leaveEvent(e)

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        rect = QRectF(self.rect()).adjusted(4, 4, -4, -4)
        if self.isChecked() or self.underMouse():
            ring = QColor(255, 255, 255) if isDarkTheme() else QColor(0, 0, 0)
            ring.setAlphaF(0.85 if self.isChecked() else 0.35)
            p.setPen(QPen(ring, 2))
            p.drawEllipse(QRectF(self.rect()).adjusted(1, 1, -1, -1))
        p.setPen(Qt.NoPen)
        p.setBrush(self.color)
        p.drawEllipse(rect)


class StatusDot(QWidget):
    COLORS = {"ok": QColor(16, 185, 89), "warn": QColor(234, 179, 8), "bad": QColor(232, 72, 72),
              "idle": QColor(150, 150, 150)}

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(10, 10)
        self.kind = "idle"

    def setKind(self, kind):
        self.kind = kind
        self.update()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        p.setPen(Qt.NoPen)
        p.setBrush(self.COLORS[self.kind])
        p.drawEllipse(self.rect().adjusted(1, 1, -1, -1))


class SceneCard(CardWidget):
    def __init__(self, name, icon, parent=None):
        super().__init__(parent)
        self.setFixedSize(168, 64)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(16, 10, 16, 10)
        lay.setSpacing(12)
        iw = IconWidget(icon, self)
        iw.setFixedSize(22, 22)
        lay.addWidget(iw)
        lay.addWidget(StrongBodyLabel(name, self))
        lay.addStretch(1)


class FormCard(SimpleCardWidget):
    """A card holding a simple label/field form and a right-aligned button row."""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.form = QFormLayout()
        self.form.setHorizontalSpacing(24)
        self.form.setVerticalSpacing(12)
        self.form.setLabelAlignment(Qt.AlignLeft | Qt.AlignVCenter)
        self.form.setFieldGrowthPolicy(QFormLayout.AllNonFixedFieldsGrow)
        self.buttons = QHBoxLayout()
        self.buttons.setSpacing(8)
        self.buttons.addStretch(1)
        v = QVBoxLayout(self)
        v.setContentsMargins(20, 18, 20, 18)
        v.setSpacing(16)
        v.addLayout(self.form)
        v.addLayout(self.buttons)

    def addRow(self, label, widget):
        lbl = BodyLabel(label, self)
        lbl.setFixedWidth(110)  # same label column in every form
        self.form.addRow(lbl, widget)

    def addButton(self, button):
        self.buttons.addWidget(button)

    def finish(self):
        # SettingCardGroup lays cards out by their current height, so pin it to the content.
        self.layout().activate()
        self.setFixedHeight(self.sizeHint().height())
        return self


class Page(ScrollArea):
    """Scrollable page with a transparent background (so Mica shows through)."""

    def __init__(self, name, parent=None):
        super().__init__(parent)
        self.setObjectName(name)
        self.view = QWidget(self)
        self.view.setObjectName("view")
        self.layout_ = QVBoxLayout(self.view)
        self.layout_.setContentsMargins(36, 28, 36, 36)
        self.layout_.setSpacing(14)
        self.layout_.setAlignment(Qt.AlignTop)
        self.setWidget(self.view)
        self.setWidgetResizable(True)
        self.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        if hasattr(self, "enableTransparentBackground"):
            self.enableTransparentBackground()
        else:
            self.setStyleSheet("QScrollArea{background:transparent;border:none}")
            self.view.setStyleSheet("QWidget#view{background:transparent}")


# ---------------------------------------------------------------- Light page

class LightPage(Page):
    def __init__(self, win):
        super().__init__("lightPage", win)
        self.win = win
        self.state = {"on": False, "mode": "white", "brightness": 100, "temp": 50,
                      "h": 0, "s": 100, "v": 100, "caps": {"colour": True, "temp": True}}
        self.confirmed = dict(self.state)
        self.last_sent = 0.0
        self._syncing = False
        L = self.layout_

        # Header
        head = QHBoxLayout()
        titles = QVBoxLayout()
        titles.setSpacing(2)
        self.title = TitleLabel("LED", self)
        status_row = QHBoxLayout()
        status_row.setSpacing(8)
        self.dot = StatusDot(self)
        self.status = CaptionLabel("Connecting…", self)
        status_row.addWidget(self.dot)
        status_row.addWidget(self.status)
        status_row.addStretch(1)
        titles.addWidget(self.title)
        titles.addLayout(status_row)
        head.addLayout(titles, 1)
        self.refresh_btn = TransparentToolButton(FIF.SYNC, self)
        self.refresh_btn.setToolTip("Refresh")
        self.refresh_btn.installEventFilter(ToolTipFilter(self.refresh_btn))
        self.refresh_btn.clicked.connect(lambda: self.refresh(force=True))
        head.addWidget(self.refresh_btn, 0, Qt.AlignTop)
        L.addLayout(head)

        # Setup prompt (shown until the app has a key or cloud credentials)
        self.setup_card = SimpleCardWidget(self)
        sl = QHBoxLayout(self.setup_card)
        sl.setContentsMargins(20, 14, 20, 14)
        icon = IconWidget(FIF.INFO, self.setup_card)
        icon.setFixedSize(20, 20)
        sl.addWidget(icon)
        sl.addWidget(BodyLabel("Connect your light to get started.", self.setup_card), 1)
        go = PrimaryPushButton("Open settings", self.setup_card)
        go.clicked.connect(lambda: win.switchTo(win.settings_page))
        sl.addWidget(go)
        self.setup_card.hide()
        L.addWidget(self.setup_card)

        # Hero: preview + power
        hero = SimpleCardWidget(self)
        hl = QHBoxLayout(hero)
        hl.setContentsMargins(20, 16, 28, 16)
        hl.setSpacing(20)
        self.preview = LightPreview(hero)
        self.preview.clicked.connect(lambda: self.setPower(not self.state["on"]))
        hl.addWidget(self.preview)
        info = QVBoxLayout()
        info.setSpacing(4)
        info.addStretch(1)
        self.summary = SubtitleLabel("Off", hero)
        self.detail = wrapped(CaptionLabel("", hero))
        info.addWidget(self.summary)
        info.addWidget(self.detail)
        info.addStretch(1)
        hl.addLayout(info, 1)
        pw = QVBoxLayout()
        pw.addStretch(1)
        self.power = SwitchButton(hero)
        self.power.setOnText("On")
        self.power.setOffText("Off")
        self.power.checkedChanged.connect(self.setPower)
        pw.addWidget(self.power)
        pw.addStretch(1)
        hl.addLayout(pw)
        L.addWidget(hero)

        # Controls
        controls = SimpleCardWidget(self)
        cl = QVBoxLayout(controls)
        cl.setContentsMargins(20, 18, 20, 20)
        cl.setSpacing(16)
        self.segment = SegmentedWidget(controls)
        self.segment.addItem("white", "White", lambda: self.setMode("white"))
        self.segment.addItem("colour", "Colour", lambda: self.setMode("colour"))
        self.segment.setFixedWidth(240)
        cl.addWidget(self.segment)
        self.stack = QStackedWidget(controls)
        self.stack.addWidget(self._whitePanel())
        self.stack.addWidget(self._colourPanel())
        cl.addWidget(self.stack)
        L.addWidget(controls)

        # Scenes
        L.addSpacing(6)
        L.addWidget(SubtitleLabel("Scenes", self))
        scenes = QWidget(self)
        flow = FlowLayout(scenes, needAni=False)
        flow.setContentsMargins(0, 0, 0, 0)
        flow.setHorizontalSpacing(10)
        flow.setVerticalSpacing(10)
        for name, icon, cmd in SCENES:
            card = SceneCard(name, icon, scenes)
            card.clicked.connect(lambda c=cmd: self.applyScene(c))
            flow.addWidget(card)
        L.addWidget(scenes)
        L.addStretch(1)

        self.render()

    # ---- panels

    def _sliderRow(self, grid, row, icon, label, left=None, right=None):
        iw = IconWidget(icon, self)
        iw.setFixedSize(18, 18)
        grid.addWidget(iw, row, 0)
        grid.addWidget(BodyLabel(label, self), row, 1)
        if left:
            grid.addWidget(CaptionLabel(left, self), row, 2)
        slider = Slider(Qt.Horizontal, self)
        slider.setMinimumWidth(200)
        grid.addWidget(slider, row, 3)
        value = CaptionLabel("", self)
        value.setMinimumWidth(56)
        grid.addWidget(value, row, 4)
        if right:
            value.setText(right)
        return slider, value

    def _whitePanel(self):
        w = QWidget(self)
        g = QGridLayout(w)
        g.setContentsMargins(0, 0, 0, 0)
        g.setHorizontalSpacing(12)
        g.setVerticalSpacing(14)
        g.setColumnStretch(3, 1)
        self.w_bright, self.w_bright_v = self._sliderRow(g, 0, FIF.BRIGHTNESS, "Brightness")
        self.w_bright.setRange(1, 100)
        self.w_temp, self.w_temp_v = self._sliderRow(g, 1, FIF.CONSTRACT, "Warmth")
        self.w_temp.setRange(0, 100)
        self.w_bright.valueChanged.connect(lambda v: self._slid("brightness", v, "white"))
        self.w_temp.valueChanged.connect(lambda v: self._slid("temp", v, "white"))
        self.temp_widgets = [g.itemAtPosition(1, c).widget() for c in (0, 1)] + [self.w_temp, self.w_temp_v]
        return w

    def _colourPanel(self):
        w = QWidget(self)
        v = QVBoxLayout(w)
        v.setContentsMargins(0, 0, 0, 0)
        v.setSpacing(14)
        sw = QWidget(w)
        flow = FlowLayout(sw, needAni=False)
        flow.setContentsMargins(0, 0, 0, 0)
        flow.setHorizontalSpacing(6)
        flow.setVerticalSpacing(6)
        self.swatches = []
        for h, s in SWATCHES:
            b = Swatch(h, s, sw)
            b.setToolTip(f"Hue {h}°")
            b.clicked.connect(lambda _=False, h=h, s=s: self.pickColour(h, s))
            flow.addWidget(b)
            self.swatches.append(b)
        custom = PushButton(FIF.PALETTE, "Custom colour…", sw)
        custom.clicked.connect(self.customColour)
        flow.addWidget(custom)
        v.addWidget(sw)
        g = QGridLayout()
        g.setHorizontalSpacing(12)
        g.setColumnStretch(3, 1)
        self.c_bright, self.c_bright_v = self._sliderRow(g, 0, FIF.BRIGHTNESS, "Brightness")
        self.c_bright.setRange(1, 100)
        self.c_bright.valueChanged.connect(lambda val: self._slid("v", val, "colour"))
        v.addLayout(g)
        return w

    # ---- rendering

    def render(self):
        st = self.state
        colour = st["mode"] == "colour"
        level = (st["v"] if colour else st["brightness"]) / 100
        col = QColor.fromHsvF(st["h"] / 360, max(st["s"], 8) / 100, 1.0) if colour else temp_to_color(st["temp"])
        self.preview.setLight(st["on"], col, level)

        if not st["on"]:
            self.summary.setText("Off")
        elif colour:
            self.summary.setText(f"Colour · {st['v']}%")
        else:
            self.summary.setText(f"{warmth_label(st['temp'])} white · {st['brightness']}%")

        # Don't blockSignals here: the Fluent slider moves its handle on valueChanged.
        # The _syncing flag stops these programmatic updates from being sent to the light.
        self._syncing = True
        try:
            self.power.setChecked(st["on"])
            if not self.w_bright.isSliderDown():
                self.w_bright.setValue(st["brightness"])
            if not self.w_temp.isSliderDown():
                self.w_temp.setValue(st["temp"])
            if not self.c_bright.isSliderDown():
                self.c_bright.setValue(max(1, st["v"]))
        finally:
            self._syncing = False
        self.w_bright_v.setText(f"{self.w_bright.value()}%")
        self.w_temp_v.setText(warmth_label(self.w_temp.value()))
        self.c_bright_v.setText(f"{self.c_bright.value()}%")

        self.segment.setCurrentItem("colour" if colour else "white")
        self.stack.setCurrentIndex(1 if colour else 0)
        self.segment.setVisible(bool(st["caps"].get("colour", True)))
        for wdg in self.temp_widgets:
            wdg.setVisible(bool(st["caps"].get("temp", True)))
        for b in self.swatches:
            b.setChecked(colour and abs(b.h - st["h"]) <= 2 and abs(b.s - st["s"]) <= 2)
            b.update()

    def setStatus(self, kind, text):
        self.dot.setKind(kind)
        self.status.setText(text)

    def setTitle(self, name):
        self.title.setText(name or "LED")

    # ---- user actions

    def send(self, cmd):
        if self.win.music.running:
            self.win.music_page.stop(restore=False)  # a manual change takes over from music sync
        self.last_sent = time.monotonic()
        self.win.worker.submit("cmd", lambda: self.win.controller.command(cmd), coalesce=True)

    def setPower(self, on):
        if self._syncing:
            return
        self.state["on"] = bool(on)
        self.render()
        self.send({"action": "power", "on": bool(on)})

    def setMode(self, mode):
        if mode == self.state["mode"] and self.state["on"]:
            return
        self.state.update(mode=mode, on=True)
        self.render()
        self.sendCurrent()

    def sendCurrent(self):
        st = self.state
        if st["mode"] == "colour":
            self.send({"action": "colour", "h": st["h"], "s": st["s"], "v": max(1, st["v"])})
        else:
            self.send({"action": "white", "brightness": st["brightness"], "temp": st["temp"]})

    def _slid(self, key, value, mode):
        if self._syncing:
            return
        self.state.update({key: value, "mode": mode, "on": True})
        self.render()
        self.sendCurrent()

    def pickColour(self, h, s):
        self.state.update(h=h, s=s, mode="colour", on=True)
        self.render()
        self.sendCurrent()

    def customColour(self):
        st = self.state
        start = QColor.fromHsvF(st["h"] / 360, st["s"] / 100, 1.0)
        dlg = ColorDialog(start, "Choose a colour", self.window())
        dlg.colorChanged.connect(lambda c: None)
        if dlg.exec():
            c = dlg.color
            h = c.hsvHue()
            self.pickColour(0 if h < 0 else h, round(c.hsvSaturationF() * 100))

    def applyScene(self, cmd):
        if cmd["action"] == "white":
            self.state.update(mode="white", on=True, brightness=cmd["brightness"], temp=cmd["temp"])
        else:
            self.state.update(mode="colour", on=True, h=cmd["h"], s=cmd["s"], v=cmd["v"])
        self.render()
        self.send(dict(cmd))

    # ---- results from the worker

    def onCommand(self, result, err):
        if err:
            self.win.error("Couldn't reach the light", err)
            if not self.win.worker.pending("cmd"):
                self.state = dict(self.confirmed)
                self.render()
            self.setStatus("bad", "Unreachable")
            return
        self.confirmed = dict(self.state)
        self.setStatus("ok", "Connected · " + ("Tuya Cloud" if result.get("via") == "cloud" else "Local network"))

    def refresh(self, force=False):
        ctl = self.win.controller
        if not ctl.configured():
            self.setup_card.show()
            self.setStatus("warn", "Needs setup")
            return
        self.setup_card.hide()
        if self.win.music.running:
            return  # don't open a second connection while music sync is streaming
        busy = self.win.worker.pending("cmd") or time.monotonic() - self.last_sent < 3
        if busy and not force:
            return
        self.win.worker.submit("state", ctl.state, coalesce=True)

    def onState(self, result, err):
        if err:
            self.setStatus("bad", "Unreachable")
            self.detail.setText(err[:160])
            return
        if self.win.worker.pending("cmd") or time.monotonic() - self.last_sent < 1.5:
            return  # a newer command is on its way; don't jump the sliders back
        self.state.update({k: result[k] for k in self.state if k in result})
        self.confirmed = dict(self.state)
        via = result.get("via")
        self.setStatus("ok", "Connected · " + ("Tuya Cloud" if via == "cloud" else "Local network"))
        d = self.win.controller.cfg["device"]
        self.detail.setText(f"{d['ip']} · " + ("via Tuya Cloud" if via == "cloud" else "over your home network"))
        self.render()


# ---------------------------------------------------------------- Music page

class Visualizer(QWidget):
    """Spectrum bars tinted with the colour currently being sent to the light."""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMinimumHeight(190)
        self.bars = [0.0] * led_music.BARS
        self.colour = QColor(150, 150, 150)
        self.level = 0.0
        self.active = False
        qconfig.themeChanged.connect(self.update)

    def setFrame(self, bars, h, s, v):
        self.bars = bars
        self.colour = QColor.fromHsvF((h % 360) / 360, max(0.0, min(1.0, s)), 1.0)
        self.level = v
        self.active = True
        self.update()

    def clear(self):
        self.bars = [0.0] * led_music.BARS
        self.active = False
        self.update()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        n = len(self.bars)
        gap = 6
        w = (self.width() - gap * (n - 1)) / n
        base = self.height()
        idle = QColor(255, 255, 255, 28) if isDarkTheme() else QColor(0, 0, 0, 22)
        if self.active:
            glow = QRadialGradient(QPointF(self.width() / 2, base), self.width() * 0.6)
            g = QColor(self.colour)
            g.setAlphaF(0.30 * max(0.15, getattr(self, "level", 0.5)))
            glow.setColorAt(0, g)
            g.setAlphaF(0)
            glow.setColorAt(1, g)
            p.setPen(Qt.NoPen)
            p.setBrush(glow)
            p.drawRoundedRect(QRectF(self.rect()), 8, 8)
        p.setPen(Qt.NoPen)
        for i, b in enumerate(self.bars):
            hgt = max(6.0, b * (base - 8))
            rect = QRectF(i * (w + gap), base - hgt, w, hgt)
            if self.active:
                c = QColor(self.colour)
                c.setAlphaF(0.45 + 0.55 * b)
                p.setBrush(c)
            else:
                p.setBrush(idle)
            p.drawRoundedRect(rect, min(4, w / 2), min(4, w / 2))


class MusicBridge(QObject):
    """Carries callbacks from the music engine's thread onto the UI thread."""
    frame = Signal(object)
    status = Signal(str, str)


class MusicPage(Page):
    SPEEDS = [10, 15, 20, 25]

    def __init__(self, win):
        super().__init__("musicPage", win)
        self.win = win
        self.music = win.music
        prefs = win.controller.cfg["app"].get("music", {})
        m = self.music
        m.style = prefs.get("style", "spectrum")
        m.sensitivity = int(prefs.get("sensitivity", 55))
        m.hue = int(prefs.get("hue", 280))
        m.fps = int(prefs.get("fps", 20))
        m.source = prefs.get("source", led_music.SYSTEM_SOURCE)
        self._restore = True
        self._before = None
        self._streaming = False
        L = self.layout_

        L.addWidget(TitleLabel("Music sync", self))
        L.addWidget(wrapped(CaptionLabel("Your light reacts to whatever is playing on this PC, "
                                         "like a music visualizer.", self)))

        hero = SimpleCardWidget(self)
        hv = QVBoxLayout(hero)
        hv.setContentsMargins(20, 18, 20, 16)
        hv.setSpacing(14)
        self.viz = Visualizer(hero)
        hv.addWidget(self.viz)
        row = QHBoxLayout()
        row.setSpacing(8)
        self.dot = StatusDot(hero)
        self.status = wrapped(CaptionLabel("Off", hero))
        row.addWidget(self.dot)
        row.addWidget(self.status, 1)
        self.toggle = PrimaryPushButton(FIF.PLAY, "Start", hero)
        self.toggle.setMinimumWidth(120)
        self.toggle.clicked.connect(lambda: self.stop() if self.music.running else self.start())
        row.addWidget(self.toggle)
        hv.addLayout(row)
        L.addWidget(hero)

        opts = SimpleCardWidget(self)
        g = QGridLayout(opts)
        g.setContentsMargins(20, 18, 20, 18)
        g.setHorizontalSpacing(16)
        g.setVerticalSpacing(16)
        g.setColumnStretch(2, 1)

        def label(r, icon, text):
            iw = IconWidget(icon, opts)
            iw.setFixedSize(18, 18)
            g.addWidget(iw, r, 0)
            lb = BodyLabel(text, opts)
            lb.setFixedWidth(100)
            g.addWidget(lb, r, 1)
            return iw, lb

        label(0, FIF.PALETTE, "Style")
        self.style_seg = SegmentedWidget(opts)
        for key, text in led_music.STYLES.items():
            self.style_seg.addItem(key, text, lambda k=key: self.setStyle(k))
        self.style_seg.setCurrentItem(m.style)
        self.style_seg.setFixedWidth(360)
        g.addWidget(self.style_seg, 0, 2, Qt.AlignLeft)
        self.style_hint = wrapped(CaptionLabel("", opts))
        g.addWidget(self.style_hint, 1, 2)

        label(2, FIF.SPEED_HIGH, "Sensitivity")
        self.sens = Slider(Qt.Horizontal, opts)
        self.sens.setRange(0, 100)
        self.sens.setValue(m.sensitivity)
        self.sens.valueChanged.connect(self.setSensitivity)
        g.addWidget(self.sens, 2, 2)

        self.colour_row = label(3, FIF.BRUSH, "Colour")
        sw = QWidget(opts)
        flow = FlowLayout(sw, needAni=False)
        flow.setContentsMargins(0, 0, 0, 0)
        flow.setHorizontalSpacing(6)
        self.swatches = []
        for h, s in SWATCHES:
            b = Swatch(h, s, sw)
            b.setChecked(abs(h - m.hue) <= 2)
            b.clicked.connect(lambda _=False, h=h: self.setHue(h))
            flow.addWidget(b)
            self.swatches.append(b)
        self.colour_widget = sw
        g.addWidget(sw, 3, 2)

        label(4, FIF.HEADPHONE, "Listen to")
        self.source_box = ComboBox(opts)
        try:
            self.sources = led_music.list_sources()
        except Exception:
            self.sources = [(led_music.SYSTEM_SOURCE, "System audio (what's playing)")]
        self.source_box.addItems([t for _, t in self.sources])
        keys = [k for k, _ in self.sources]
        self.source_box.setCurrentIndex(keys.index(m.source) if m.source in keys else 0)
        self.source_box.currentIndexChanged.connect(self.setSource)
        self.source_box.setMinimumWidth(320)
        g.addWidget(self.source_box, 4, 2, Qt.AlignLeft)

        label(5, FIF.SYNC, "Update speed")
        self.speed_box = ComboBox(opts)
        self.speed_box.addItems([f"{n} updates per second" for n in self.SPEEDS])
        self.speed_box.setCurrentIndex(self.SPEEDS.index(m.fps) if m.fps in self.SPEEDS else 2)
        self.speed_box.currentIndexChanged.connect(self.setSpeed)
        g.addWidget(self.speed_box, 5, 2, Qt.AlignLeft)
        L.addWidget(opts)

        L.addWidget(wrapped(CaptionLabel("Works over your home network only. When you stop, the light goes "
                                         "back to how it was. Changing the light on the Light page also stops "
                                         "music sync.", self)))
        L.addStretch(1)

        win.music_bridge.frame.connect(self.onFrame)
        win.music_bridge.status.connect(self.onStatus)
        self._updateStyleUi()

    # ---- settings

    def _save(self):
        m = self.music
        self.win.controller.set_app_pref("music", {"style": m.style, "sensitivity": m.sensitivity,
                                                   "hue": m.hue, "fps": m.fps, "source": m.source})

    def _updateStyleUi(self):
        hints = {"pulse": "One colour that breathes with the volume.",
                 "spectrum": "Bass shows red, mids green, treble blue.",
                 "beat": "A new colour and a flash on every beat.",
                 "party": "Colours spin with the energy and flash on beats."}
        self.style_hint.setText(hints[self.music.style])
        for wdg in self.colour_row + (self.colour_widget,):
            wdg.setVisible(self.music.style == "pulse")

    def setStyle(self, key):
        self.music.style = key
        self._updateStyleUi()
        self._save()

    def setSensitivity(self, v):
        self.music.sensitivity = v
        self._save()

    def setHue(self, h):
        self.music.hue = h
        self._save()

    def setSource(self, i):
        self.music.source = self.sources[i][0]
        self._save()

    def setSpeed(self, i):
        self.music.fps = self.SPEEDS[i]
        self._save()

    # ---- start / stop

    def start(self):
        if self.music.running:
            return
        if self.win.worker.pending("cmd"):
            QTimer.singleShot(300, self.start)  # let a queued light command finish first
            return
        self._before = dict(self.win.light_page.confirmed)
        self._restore = True
        self._streaming = False
        self.toggle.setText("Stop")
        self.toggle.setIcon(FIF.PAUSE)
        self.music.start()
        self.win.updateMusicAction(True)

    def stop(self, restore=True):
        self._restore = restore
        self.music.stop(wait=True)

    def onFrame(self, f):
        h, s, v = f["colour"]
        self.viz.setFrame(f["bars"], h, s, v)

    def onStatus(self, kind, text):
        if kind == "stopped":
            self.toggle.setText("Start")
            self.toggle.setIcon(FIF.PLAY)
            self.win.updateMusicAction(False)
            self.viz.clear()
            if self.dot.kind != "bad":
                self.dot.setKind("idle")
                self.status.setText("Off")
            if self._restore and self._before and self._streaming:
                lp = self.win.light_page
                lp.state = dict(self._before)
                lp.render()
                lp.sendCurrent() if lp.state["on"] else lp.send({"action": "power", "on": False})
            self._before = None
            return
        if kind == "error":
            self.dot.setKind("bad")
            self.win.error("Music sync", text)
        else:
            self.dot.setKind({"ok": "ok", "busy": "warn"}.get(kind, "idle"))
            if kind == "ok":
                self._streaming = True
                text += f" · {self.music.fps} updates/s"
        self.status.setText(text)


# ---------------------------------------------------------------- Settings page

class SettingsPage(Page):
    REGIONS = [("eu", "Central Europe"), ("eu-w", "Western Europe"), ("us", "Western America"),
               ("us-e", "Eastern America"), ("in", "India"), ("sg", "Singapore"), ("cn", "China")]
    CONNECTIONS = [("auto", "Automatic"), ("local", "Home network only"), ("cloud", "Tuya Cloud only")]
    THEMES = [("auto", "Use system setting"), ("light", "Light"), ("dark", "Dark")]

    def __init__(self, win):
        super().__init__("settingsPage", win)
        self.win = win
        ctl = win.controller
        L = self.layout_
        L.addWidget(TitleLabel("Settings", self))

        # Connection
        g = SettingCardGroup("Connection", self.view)
        card = SettingCard(FIF.WIFI, "Connect via",
                           "Automatic uses your home network first and Tuya Cloud as a backup", g)
        self.conn = ComboBox(card)
        self.conn.addItems([t for _, t in self.CONNECTIONS])
        self.conn.setCurrentIndex([k for k, _ in self.CONNECTIONS].index(ctl.cfg.get("connection", "auto")))
        self.conn.currentIndexChanged.connect(self.saveConnection)
        card.hBoxLayout.addWidget(self.conn, 0, Qt.AlignRight)
        card.hBoxLayout.addSpacing(16)
        g.addSettingCard(card)
        self.check_card = PushSettingCard("Run check", FIF.SPEED_HIGH, "Connection check",
                                          "Looks for the light and tests each way of reaching it", g)
        self.check_card.clicked.connect(self.runCheck)
        g.addSettingCard(self.check_card)
        L.addWidget(g)

        # Device
        g = SettingCardGroup("Device", self.view)
        self.dev_form = FormCard(g)
        self.dev_id, self.dev_ip = LineEdit(self.dev_form), LineEdit(self.dev_form)
        self.dev_ver = ComboBox(self.dev_form)
        self.dev_ver.addItems(["3.5", "3.4", "3.3", "3.1"])
        self.dev_key = PasswordLineEdit(self.dev_form)
        self.dev_form.addRow("Device ID", self.dev_id)
        self.dev_form.addRow("IP address", self.dev_ip)
        self.dev_form.addRow("Protocol", self.dev_ver)
        self.dev_form.addRow("Local key", self.dev_key)
        save = PrimaryPushButton(FIF.SAVE, "Save", self.dev_form)
        save.clicked.connect(self.saveDevice)
        self.dev_form.addButton(save)
        g.addSettingCard(self.dev_form.finish())
        L.addWidget(g)

        # Cloud
        g = SettingCardGroup("Tuya Cloud", self.view)
        self.cloud_form = FormCard(g)
        self.region = ComboBox(self.cloud_form)
        self.region.addItems([f"{t} ({k})" for k, t in self.REGIONS])
        self.cloud_id = LineEdit(self.cloud_form)
        self.cloud_secret = PasswordLineEdit(self.cloud_form)
        self.cloud_form.addRow("Data centre", self.region)
        self.cloud_form.addRow("Access ID", self.cloud_id)
        self.cloud_form.addRow("Access Secret", self.cloud_secret)
        csave = PushButton("Save", self.cloud_form)
        csave.clicked.connect(lambda: self.saveCloud(fetch=False))
        self.fetch_btn = PrimaryPushButton(FIF.DOWNLOAD, "Save && get local key", self.cloud_form)
        self.fetch_btn.clicked.connect(lambda: self.saveCloud(fetch=True))
        self.cloud_form.addButton(csave)
        self.cloud_form.addButton(self.fetch_btn)
        g.addSettingCard(self.cloud_form.finish())
        L.addWidget(g)

        # App
        g = SettingCardGroup("App", self.view)
        card = SettingCard(FIF.BRUSH, "Theme", "Light, dark, or follow Windows", g)
        self.theme = ComboBox(card)
        self.theme.addItems([t for _, t in self.THEMES])
        self.theme.setCurrentIndex([k for k, _ in self.THEMES].index(ctl.cfg["app"].get("theme", "auto")))
        self.theme.currentIndexChanged.connect(self.saveTheme)
        card.hBoxLayout.addWidget(self.theme, 0, Qt.AlignRight)
        card.hBoxLayout.addSpacing(16)
        g.addSettingCard(card)
        card = SettingCard(FIF.MINIMIZE, "Keep running in the tray",
                           "Closing the window leaves LED Control in the notification area", g)
        self.tray_sw = SwitchButton(card)
        self.tray_sw.setOnText("On")
        self.tray_sw.setOffText("Off")
        self.tray_sw.setChecked(bool(ctl.cfg["app"].get("close_to_tray", True)))
        self.tray_sw.checkedChanged.connect(lambda v: ctl.set_app_pref("close_to_tray", bool(v)))
        card.hBoxLayout.addWidget(self.tray_sw, 0, Qt.AlignRight)
        card.hBoxLayout.addSpacing(16)
        g.addSettingCard(card)
        card = SettingCard(FIF.POWER_BUTTON, "Start with Windows",
                           "Opens quietly in the tray when you sign in", g)
        self.startup_sw = SwitchButton(card)
        self.startup_sw.setOnText("On")
        self.startup_sw.setOffText("Off")
        self.startup_sw.setChecked(startup_enabled())
        self.startup_sw.checkedChanged.connect(self.saveStartup)
        card.hBoxLayout.addWidget(self.startup_sw, 0, Qt.AlignRight)
        card.hBoxLayout.addSpacing(16)
        g.addSettingCard(card)
        L.addWidget(g)

        # About
        g = SettingCardGroup("About", self.view)
        about = PushSettingCard("Open folder", FIF.INFO, f"{APP_NAME} {APP_VERSION}",
                                f"Settings are stored in {CONFIG_DIR}", g)
        about.clicked.connect(lambda: os.startfile(CONFIG_DIR) if CONFIG_DIR.exists() else None)
        g.addSettingCard(about)
        L.addWidget(g)
        L.addStretch(1)

        self.loadFields()

    def loadFields(self):
        cfg = self.win.controller.cfg
        d, c = cfg["device"], cfg["cloud"]
        self.dev_id.setText(d["id"])
        self.dev_ip.setText(d["ip"])
        self.dev_ver.setCurrentText(str(d["version"]))
        self.dev_key.clear()
        self.dev_key.setPlaceholderText("Saved · leave blank to keep" if d["key"] else "Not set")
        keys = [k for k, _ in self.REGIONS]
        self.region.setCurrentIndex(keys.index(c["region"]) if c["region"] in keys else 0)
        self.cloud_id.clear()
        self.cloud_id.setPlaceholderText("Saved · leave blank to keep" if c["api_key"] else "From your Tuya project's Overview page")
        self.cloud_secret.clear()
        self.cloud_secret.setPlaceholderText("Saved · leave blank to keep" if c["api_secret"] else "")

    # ---- actions (all device/cloud work runs on the worker)

    def saveConnection(self, i):
        key = self.CONNECTIONS[i][0]
        self.win.worker.submit("save", lambda: self.win.controller.update_config({"connection": key}))

    def saveDevice(self):
        body = {"device": {"id": self.dev_id.text(), "ip": self.dev_ip.text(),
                           "version": self.dev_ver.currentText(), "key": self.dev_key.text()}}
        self.win.worker.submit("save", lambda: self.win.controller.update_config(body))

    def saveCloud(self, fetch):
        body = {"cloud": {"region": self.REGIONS[self.region.currentIndex()][0],
                          "api_key": self.cloud_id.text(), "api_secret": self.cloud_secret.text()}}
        ctl = self.win.controller

        def job():
            ctl.update_config(body)
            return ctl.fetch_key_from_cloud() if fetch else None

        if fetch:
            self.fetch_btn.setEnabled(False)
            self.fetch_btn.setText("Connecting to Tuya…")
        self.win.worker.submit("fetch" if fetch else "save", job)

    def saveTheme(self, i):
        key = self.THEMES[i][0]
        self.win.controller.set_app_pref("theme", key)
        apply_theme(key)

    def saveStartup(self, on):
        try:
            set_startup(bool(on))
        except OSError as e:
            self.win.error("Couldn't change startup setting", str(e))
            self.startup_sw.blockSignals(True)
            self.startup_sw.setChecked(startup_enabled())
            self.startup_sw.blockSignals(False)

    def runCheck(self):
        self.check_card.button.setEnabled(False)
        self.check_card.button.setText("Checking…")
        self.win.worker.submit("diag", self.win.controller.diagnostics)

    # ---- results

    def onSaved(self, result, err):
        if err:
            self.win.error("Couldn't save", err)
        else:
            self.win.success("Saved", "")
        self.loadFields()
        self.win.light_page.setTitle(self.win.controller.cfg["device"]["name"])
        self.win.light_page.refresh(force=True)

    def onFetched(self, result, err):
        self.fetch_btn.setEnabled(True)
        self.fetch_btn.setText("Save && get local key")
        self.loadFields()
        if err:
            self.win.error("Tuya Cloud", err)
            return
        name = (result or {}).get("name") or "your light"
        self.win.success("Connected", f"Saved the local key for {name}.")
        self.win.light_page.setTitle(self.win.controller.cfg["device"]["name"])
        self.win.light_page.refresh(force=True)

    def onDiagnostics(self, r, err):
        self.check_card.button.setEnabled(True)
        self.check_card.button.setText("Run check")
        if err:
            self.win.error("Connection check failed", err)
            return
        mark = lambda ok: "✓" if ok else ("✕" if ok is False else "–")
        lines = [
            f"{mark(r['broadcast_seen'])}  Light found on your network"
            + (f" ({r.get('broadcast_ip')})" if r["broadcast_seen"] else ""),
            f"{mark(r['tcp_reachable'])}  PC can reach the light directly",
            f"{mark(r['has_local_key'] or None)}  Local key saved",
        ]
        if "local_ok" in r:
            lines.append(f"{mark(r['local_ok'])}  Home-network control works"
                         + (f"\n      {r['local_error']}" if r.get("local_error") else ""))
        if r["has_cloud_creds"]:
            lines.append(f"{mark(r.get('cloud_ok'))}  Tuya Cloud control works"
                         + (f"\n      {r['cloud_error']}" if r.get("cloud_error") else ""))
        else:
            lines.append("–  Tuya Cloud not set up")
        if r.get("ip_updated"):
            lines.append("\nThe light's IP address changed; it has been updated.")
            self.loadFields()
        box = MessageBox("Connection check", "\n".join(lines), self.window())
        box.yesButton.setText("OK")
        box.cancelButton.hide()
        box.exec()
        self.win.light_page.refresh(force=True)


# ---------------------------------------------------------------- Windows integration

def launch_command():
    if getattr(sys, "frozen", False):
        return f'"{sys.executable}" --minimized'
    pyw = Path(sys.executable).with_name("pythonw.exe")
    return f'"{pyw if pyw.exists() else sys.executable}" "{Path(__file__).resolve()}" --minimized'


def startup_enabled():
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as k:
            winreg.QueryValueEx(k, APP_NAME)
            return True
    except OSError:
        return False


def set_startup(on):
    import winreg
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as k:
        if on:
            winreg.SetValueEx(k, APP_NAME, 0, winreg.REG_SZ, launch_command())
        else:
            try:
                winreg.DeleteValue(k, APP_NAME)
            except FileNotFoundError:
                pass


def apply_theme(key):
    setTheme({"light": Theme.LIGHT, "dark": Theme.DARK}.get(key, Theme.AUTO))


# ---------------------------------------------------------------- main window

class MainWindow(FluentWindow):
    def __init__(self, controller, start_hidden=False):
        super().__init__()
        self.controller = controller
        self.worker = Worker()
        self.worker.finished.connect(self.onJobDone)
        self._quitting = False
        self._tray_hint_shown = False

        self.icon = QIcon(resource("icon.ico"))
        self.setWindowIcon(self.icon)
        self.setWindowTitle(APP_NAME)
        self.resize(980, 760)
        self.setMinimumSize(720, 560)
        if hasattr(self, "setMicaEffectEnabled"):
            self.setMicaEffectEnabled(True)

        self.music_bridge = MusicBridge()
        self.music = led_music.MusicSync(lambda: dict(controller.cfg["device"]),
                                         on_frame=self.music_bridge.frame.emit,
                                         on_status=self.music_bridge.status.emit)

        self.light_page = LightPage(self)
        self.music_page = MusicPage(self)
        self.settings_page = SettingsPage(self)
        self.addSubInterface(self.light_page, FIF.HOME, "Light")
        self.addSubInterface(self.music_page, FIF.MUSIC, "Music sync")
        self.addSubInterface(self.settings_page, FIF.SETTING, "Settings", NavigationItemPosition.BOTTOM)
        self.light_page.setTitle(controller.cfg["device"]["name"])

        self.theme_listener = SystemThemeListener(self)
        self.theme_listener.start()

        self._makeTray()
        self.poll = QTimer(self)
        self.poll.timeout.connect(lambda: self.isVisible() and self.light_page.refresh())
        self.poll.start(POLL_MS)

        screen = QApplication.primaryScreen().availableGeometry()
        self.move(screen.center() - self.rect().center())
        if not start_hidden or not QSystemTrayIcon.isSystemTrayAvailable():
            self.show()
        self.light_page.refresh(force=True)

    # ---- tray

    def _makeTray(self):
        self.tray = QSystemTrayIcon(self.icon, self)
        self.tray.setToolTip(APP_NAME)
        menu = SystemTrayMenu(parent=self)
        lp = self.light_page
        menu.addAction(Action(FIF.HOME, "Open LED Control", triggered=self.showWindow))
        menu.addSeparator()
        menu.addAction(Action(FIF.POWER_BUTTON, "Turn on", triggered=lambda: lp.setPower(True)))
        menu.addAction(Action(FIF.CLOSE, "Turn off", triggered=lambda: lp.setPower(False)))
        scenes = RoundMenu("Scenes", self)
        scenes.setIcon(FIF.PALETTE)
        for name, icon, cmd in SCENES:
            scenes.addAction(Action(icon, name, triggered=lambda _=False, c=cmd: lp.applyScene(c)))
        menu.addMenu(scenes)
        self.music_action = Action(FIF.MUSIC, "Start music sync", triggered=self._toggleMusic)
        menu.addAction(self.music_action)
        menu.addSeparator()
        menu.addAction(Action(FIF.EMBED, "Quit", triggered=self.quitApp))
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self._trayActivated)
        self.tray.show()

    def _toggleMusic(self):
        if self.music.running:
            self.music_page.stop()
        else:
            self.music_page.start()

    def updateMusicAction(self, running):
        self.music_action.setText("Stop music sync" if running else "Start music sync")
        self.tray.setToolTip(f"{APP_NAME} · music sync on" if running else APP_NAME)

    def _trayActivated(self, reason):
        if reason == QSystemTrayIcon.Trigger:
            if self.isVisible() and not self.isMinimized():
                self.hide()
            else:
                self.showWindow()

    def showWindow(self):
        self.show()
        self.setWindowState((self.windowState() & ~Qt.WindowMinimized) | Qt.WindowActive)
        self.raise_()
        self.activateWindow()
        self.light_page.refresh(force=True)

    def quitApp(self):
        self._quitting = True
        self.close()

    def closeEvent(self, e):
        keep = self.controller.cfg["app"].get("close_to_tray", True)
        if not self._quitting and keep and QSystemTrayIcon.isSystemTrayAvailable():
            e.ignore()
            self.hide()
            if not self._tray_hint_shown:
                self._tray_hint_shown = True
                self.tray.showMessage(APP_NAME, "Still running in the notification area. "
                                      "Right-click the bulb icon for quick controls or to quit.",
                                      self.icon, 4000)
            return
        self.poll.stop()
        if self.music.running:
            # Quitting mid-music: stop streaming and put the light back the way it was.
            before = self.music_page._before
            self.music.stop(wait=True)
            if before:
                try:
                    if before["on"]:
                        cmd = ({"action": "colour", "h": before["h"], "s": before["s"], "v": max(1, before["v"])}
                               if before["mode"] == "colour" else
                               {"action": "white", "brightness": before["brightness"], "temp": before["temp"]})
                    else:
                        cmd = {"action": "power", "on": False}
                    self.controller.command(cmd)
                except Exception:
                    pass
        self.worker.stop()
        self.theme_listener.terminate()
        self.theme_listener.deleteLater()
        self.tray.hide()
        super().closeEvent(e)
        QApplication.quit()

    # ---- messages

    def error(self, title, content):
        InfoBar.error(title, content, orient=Qt.Vertical, isClosable=True, duration=6000,
                      position=InfoBarPosition.TOP_RIGHT, parent=self)

    def success(self, title, content):
        InfoBar.success(title, content, isClosable=True, duration=2500,
                        position=InfoBarPosition.TOP_RIGHT, parent=self)

    def onJobDone(self, tag, result, err):
        handlers = {
            "cmd": self.light_page.onCommand,
            "state": self.light_page.onState,
            "save": self.settings_page.onSaved,
            "fetch": self.settings_page.onFetched,
            "diag": self.settings_page.onDiagnostics,
        }
        handlers[tag](result, err)


# ---------------------------------------------------------------- entry point

def self_test(app, out_path):
    """Build check: start hidden (no single-instance lock), read the light, write a JSON report."""
    import json
    report = {"version": APP_VERSION, "frozen": bool(getattr(sys, "frozen", False)),
              "icon_found": Path(resource("icon.ico")).exists()}
    try:
        win = MainWindow(Controller(), start_hidden=True)
    except Exception as e:
        report["error"] = f"window: {e!r}"
        Path(out_path).write_text(json.dumps(report, indent=2), encoding="utf-8")
        return 1

    def finish():
        lp = win.light_page
        report["status"] = lp.status.text()
        report["state"] = {k: lp.state[k] for k in ("on", "mode", "brightness", "temp", "h", "s", "v")}
        report["tray_available"] = QSystemTrayIcon.isSystemTrayAvailable()
        try:  # music sync: can we list audio sources and capture a moment of system audio?
            import soundcard as sc
            report["audio_sources"] = len(led_music.list_sources())
            spk = sc.default_speaker()
            mic = sc.get_microphone(id=str(spk.name), include_loopback=True)
            with mic.recorder(samplerate=led_music.SAMPLE_RATE, channels=2) as rec:
                report["audio_capture"] = list(rec.record(numframes=led_music.BLOCK).shape)
        except Exception as e:
            report["audio_capture"] = f"error: {e!r}"
        Path(out_path).write_text(json.dumps(report, indent=2), encoding="utf-8")
        win._quitting = True
        win.close()

    QTimer.singleShot(5000, finish)
    return app.exec()


def uninstall(app):
    """Run from Settings > Apps > Uninstall. Removes shortcut, registry entries and the
    install folder; keeps the settings in %APPDATA%."""
    import subprocess
    import winreg
    apply_theme("auto")
    box = MessageBox(f"Uninstall {APP_NAME}?",
                     "This removes the app, its Start menu shortcut and the startup entry.\n"
                     f"Your settings stay in {CONFIG_DIR}.", None)
    box.yesButton.setText("Uninstall")
    box.cancelButton.setText("Cancel")
    if not box.exec():
        return 0
    lnk = Path(os.environ["APPDATA"]) / "Microsoft" / "Windows" / "Start Menu" / "Programs" / f"{APP_NAME}.lnk"
    lnk.unlink(missing_ok=True)
    for key, value in ((rf"Software\Microsoft\Windows\CurrentVersion\Uninstall\{APP_NAME}", None),
                       (RUN_KEY, APP_NAME)):
        try:
            if value is None:
                winreg.DeleteKey(winreg.HKEY_CURRENT_USER, key)
            else:
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key, 0, winreg.KEY_SET_VALUE) as k:
                    winreg.DeleteValue(k, value)
        except FileNotFoundError:
            pass
    folder = Path(sys.executable).parent
    if getattr(sys, "frozen", False) and folder.name == APP_NAME and folder.parent.name == "Programs":
        # The running .exe can't delete itself; a detached cmd removes the folder once we exit.
        subprocess.Popen(f'cmd /c ping -n 3 127.0.0.1 >nul & rmdir /s /q "{folder}"',
                         creationflags=0x08000000 | 0x00000008)  # CREATE_NO_WINDOW | DETACHED_PROCESS
    return 0


def main():
    if sys.platform == "win32":
        try:
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(APP_ID)
        except (AttributeError, OSError):
            pass

    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setQuitOnLastWindowClosed(False)

    if "--self-test" in sys.argv:
        return self_test(app, sys.argv[sys.argv.index("--self-test") + 1])
    if "--uninstall" in sys.argv:
        return uninstall(app)

    # Single instance: if already running, ask that copy to show itself.
    sock = QLocalSocket()
    sock.connectToServer(INSTANCE_KEY)
    if sock.waitForConnected(300):
        sock.write(b"show")
        sock.flush()
        sock.waitForBytesWritten(300)
        return 0

    controller = Controller()
    apply_theme(controller.cfg["app"].get("theme", "auto"))
    try:
        from qframelesswindow.utils import getSystemAccentColor
        accent = getSystemAccentColor()
        if accent.isValid():
            setThemeColor(accent, save=False)
    except Exception:
        pass

    win = MainWindow(controller, start_hidden="--minimized" in sys.argv)

    server = QLocalServer()
    QLocalServer.removeServer(INSTANCE_KEY)
    server.listen(INSTANCE_KEY)
    server.newConnection.connect(lambda: (server.nextPendingConnection(), win.showWindow()))

    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
