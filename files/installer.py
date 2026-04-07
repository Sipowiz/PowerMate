"""
Griffin PowerMate - Installer
Checks dependencies, installs them, sets up the driver.
Run this once to get everything going.
"""

import sys
import os
import subprocess
import threading
import tkinter as tk
from tkinter import ttk
import importlib

REQUIRED = [
    ("hid",      "hid"),
    ("pycaw",    "pycaw"),
    ("comtypes", "comtypes"),
    ("PIL",      "Pillow"),
    ("pystray",  "pystray"),
]

BASE_DIR    = os.path.dirname(os.path.abspath(__file__))
DRIVER_FILE = os.path.join(BASE_DIR, "powermate_tray.py")
APP_NAME    = "Griffin PowerMate Driver"

# ── Colors / fonts ─────────────────────────────────────────────────────────────
BG       = "#0e0e0e"
BG2      = "#161616"
BG3      = "#1e1e1e"
ACCENT   = "#4fa3e8"
ACCENT2  = "#2a6fc1"
TEXT     = "#e8e8e8"
MUTED    = "#666666"
SUCCESS  = "#44bb66"
ERROR    = "#ee4444"
WARN     = "#f0a020"
FONT     = ("Segoe UI", 10)
FONT_B   = ("Segoe UI", 10, "bold")
FONT_LG  = ("Segoe UI", 14, "bold")
FONT_XL  = ("Segoe UI", 22, "bold")
MONO     = ("Consolas", 9)


class InstallerApp:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title("PowerMate Installer")
        self.root.configure(bg=BG)
        self.root.resizable(False, False)
        self._page = None

        self.root.update_idletasks()
        w, h = 520, 560
        sw = self.root.winfo_screenwidth()
        sh = self.root.winfo_screenheight()
        self.root.geometry(f"{w}x{h}+{(sw-w)//2}+{(sh-h)//2}")

        self._build_ui()
        self.show_welcome()
        self.root.mainloop()

    # ── Layout skeleton ────────────────────────────────────────────────────────
    def _build_ui(self):
        # Top bar
        bar = tk.Frame(self.root, bg="#080808", height=60)
        bar.pack(fill="x")
        bar.pack_propagate(False)

        logo_c = tk.Canvas(bar, width=40, height=40, bg="#080808",
                            highlightthickness=0)
        logo_c.pack(side="left", padx=20, pady=10)
        logo_c.create_oval(3, 3, 37, 37, outline=ACCENT, width=3)
        logo_c.create_oval(13, 13, 27, 27, fill=ACCENT, outline="")

        tk.Label(bar, text="Griffin PowerMate", font=FONT_B,
                 bg="#080808", fg=TEXT).pack(side="left")
        tk.Label(bar, text=" Installer", font=FONT,
                 bg="#080808", fg=MUTED).pack(side="left")

        # Step indicator
        self.step_frame = tk.Frame(bar, bg="#080808")
        self.step_frame.pack(side="right", padx=20)
        self.step_labels = []
        for i, txt in enumerate(["Welcome", "Install", "Launch"]):
            f = tk.Frame(self.step_frame, bg="#080808")
            f.pack(side="left", padx=4)
            dot = tk.Label(f, text="●", font=("Segoe UI", 8),
                           bg="#080808", fg=MUTED)
            dot.pack()
            lbl = tk.Label(f, text=txt, font=("Segoe UI", 7),
                           bg="#080808", fg=MUTED)
            lbl.pack()
            self.step_labels.append((dot, lbl))

        tk.Frame(self.root, bg="#222222", height=1).pack(fill="x")

        # Content area
        self.content = tk.Frame(self.root, bg=BG)
        self.content.pack(fill="both", expand=True)

        # Bottom bar
        tk.Frame(self.root, bg="#222222", height=1).pack(fill="x")
        self.bottom = tk.Frame(self.root, bg=BG2, height=56)
        self.bottom.pack(fill="x")
        self.bottom.pack_propagate(False)

    def _set_step(self, n):
        for i, (dot, lbl) in enumerate(self.step_labels):
            if i < n:
                dot.configure(fg=SUCCESS); lbl.configure(fg=SUCCESS)
            elif i == n:
                dot.configure(fg=ACCENT);  lbl.configure(fg=ACCENT)
            else:
                dot.configure(fg=MUTED);   lbl.configure(fg=MUTED)

    def _clear_content(self):
        for w in self.content.winfo_children():
            w.destroy()
        for w in self.bottom.winfo_children():
            w.destroy()

    def _btn(self, parent, text, command, primary=False, **kw):
        bg  = ACCENT2 if primary else BG3
        fg  = "white"  if primary else TEXT
        hbg = ACCENT   if primary else "#2a2a2a"

        b = tk.Label(parent, text=text, font=FONT_B if primary else FONT,
                     bg=bg, fg=fg, padx=20, pady=8, cursor="hand2", **kw)
        b.bind("<Enter>",   lambda e: b.configure(bg=hbg))
        b.bind("<Leave>",   lambda e: b.configure(bg=bg))
        b.bind("<Button-1>", lambda e: command())
        return b

    # ── Pages ──────────────────────────────────────────────────────────────────
    def show_welcome(self):
        self._clear_content()
        self._set_step(0)

        pad = tk.Frame(self.content, bg=BG, padx=40, pady=30)
        pad.pack(fill="both", expand=True)

        # Knob illustration
        c = tk.Canvas(pad, width=90, height=90, bg=BG, highlightthickness=0)
        c.pack(pady=(0, 20))
        c.create_oval(5, 5, 85, 85, outline=ACCENT, width=3)
        c.create_oval(25, 25, 65, 65, fill=BG3, outline=ACCENT2, width=2)
        c.create_oval(38, 38, 52, 52, fill=ACCENT, outline="")
        # tick marks
        import math
        for deg in range(0, 360, 30):
            r1, r2 = 36, 42
            a = math.radians(deg)
            x0 = 45 + r1*math.cos(a); y0 = 45 + r1*math.sin(a)
            x1 = 45 + r2*math.cos(a); y1 = 45 + r2*math.sin(a)
            c.create_line(x0, y0, x1, y1, fill=MUTED, width=1)

        tk.Label(pad, text="Welcome", font=FONT_XL,
                 bg=BG, fg=TEXT).pack()
        tk.Label(pad, text="This installer will set up the PowerMate driver\nand its dependencies on your system.",
                 font=FONT, bg=BG, fg=MUTED, justify="center").pack(pady=(8, 24))

        # Dependency check
        check_frame = tk.Frame(pad, bg=BG2, padx=16, pady=14)
        check_frame.pack(fill="x")

        tk.Label(check_frame, text="DEPENDENCIES", font=("Segoe UI", 8),
                 bg=BG2, fg=MUTED).pack(anchor="w", pady=(0, 8))

        self._dep_labels = {}
        for mod, pkg in REQUIRED:
            row = tk.Frame(check_frame, bg=BG2)
            row.pack(fill="x", pady=2)
            tk.Label(row, text=pkg, font=MONO, bg=BG2, fg=TEXT,
                     width=14, anchor="w").pack(side="left")
            try:
                importlib.import_module(mod)
                status = ("✓ installed", SUCCESS)
            except ImportError:
                status = ("○ not found", WARN)
            lbl = tk.Label(row, text=status[0], font=MONO,
                           bg=BG2, fg=status[1])
            lbl.pack(side="left")
            self._dep_labels[pkg] = lbl

        # Bottom buttons
        self._btn(self.bottom, "Continue →", self.show_install,
                  primary=True).pack(side="right", padx=16, pady=10)

    def show_install(self):
        self._clear_content()
        self._set_step(1)

        pad = tk.Frame(self.content, bg=BG, padx=40, pady=30)
        pad.pack(fill="both", expand=True)

        tk.Label(pad, text="Installing dependencies", font=FONT_LG,
                 bg=BG, fg=TEXT).pack(anchor="w")
        tk.Label(pad, text="Installing required Python packages via pip…",
                 font=FONT, bg=BG, fg=MUTED).pack(anchor="w", pady=(4, 20))

        # Log area
        log_frame = tk.Frame(pad, bg="#0a0a0a", padx=1, pady=1)
        log_frame.pack(fill="both", expand=True)
        self.log_text = tk.Text(log_frame, font=MONO, bg="#0a0a0a", fg="#aaaaaa",
                                 insertbackground=ACCENT, relief="flat",
                                 state="disabled", wrap="word", height=12)
        self.log_text.pack(fill="both", expand=True, padx=8, pady=8)
        sb = tk.Scrollbar(log_frame, command=self.log_text.yview)
        self.log_text.configure(yscrollcommand=sb.set)

        # Progress bar
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Blue.Horizontal.TProgressbar",
                         troughcolor=BG3, background=ACCENT,
                         bordercolor=BG, lightcolor=ACCENT, darkcolor=ACCENT2)
        self.progress = ttk.Progressbar(pad, style="Blue.Horizontal.TProgressbar",
                                          mode="indeterminate", length=440)
        self.progress.pack(pady=(12, 0))

        self.install_btn = self._btn(self.bottom, "Install", self._run_install,
                                      primary=True)
        self.install_btn.pack(side="right", padx=16, pady=10)

    def _log(self, text, color=None):
        self.log_text.configure(state="normal")
        self.log_text.insert("end", text + "\n")
        self.log_text.configure(state="disabled")
        self.log_text.see("end")

    def _run_install(self):
        self.install_btn.configure(state="disabled")
        self.progress.start(12)
        threading.Thread(target=self._install_worker, daemon=True).start()

    def _install_worker(self):
        pkgs = [pkg for mod, pkg in REQUIRED
                if not self._is_installed(mod)]

        if not pkgs:
            self.root.after(0, lambda: self._log("✓ All packages already installed.", SUCCESS))
            self.root.after(500, self.show_launch)
            return

        for pkg in pkgs:
            self.root.after(0, lambda p=pkg: self._log(f"→ Installing {p}…"))
            result = subprocess.run(
                [sys.executable, "-m", "pip", "install", pkg],
                capture_output=True, text=True
            )
            if result.returncode == 0:
                self.root.after(0, lambda p=pkg: self._log(f"  ✓ {p} installed", SUCCESS))
            else:
                err = result.stderr.strip().splitlines()[-1] if result.stderr else "Unknown error"
                self.root.after(0, lambda p=pkg, e=err: self._log(f"  ✗ {p} failed: {e}", ERROR))

        self.root.after(0, lambda: self.progress.stop())
        self.root.after(800, self.show_launch)

    def _is_installed(self, mod):
        try:
            importlib.import_module(mod)
            return True
        except ImportError:
            return False

    def show_launch(self):
        self._clear_content()
        self._set_step(2)

        pad = tk.Frame(self.content, bg=BG, padx=40, pady=30)
        pad.pack(fill="both", expand=True)

        # Success icon
        c = tk.Canvas(pad, width=64, height=64, bg=BG, highlightthickness=0)
        c.pack(pady=(0, 16))
        c.create_oval(4, 4, 60, 60, fill=SUCCESS, outline="")
        c.create_line(18, 32, 28, 44, fill="white", width=4)
        c.create_line(28, 44, 48, 20, fill="white", width=4)

        tk.Label(pad, text="Ready to go!", font=FONT_XL,
                 bg=BG, fg=TEXT).pack()
        tk.Label(pad,
                 text="The PowerMate driver is installed.\nConfigure your options below, then launch.",
                 font=FONT, bg=BG, fg=MUTED, justify="center").pack(pady=(8, 24))

        # Options
        opts = tk.Frame(pad, bg=BG2, padx=20, pady=16)
        opts.pack(fill="x")

        self._startup_var = tk.BooleanVar(value=True)
        self._launch_var  = tk.BooleanVar(value=True)

        self._opt_row(opts, "Start with Windows automatically",
                       "Add to Windows startup", self._startup_var, row=0)
        self._opt_row(opts, "Launch driver now",
                       "Start the tray app immediately", self._launch_var, row=1)

        # Buttons
        self._btn(self.bottom, "← Back", self.show_install).pack(
            side="left", padx=16, pady=10)
        self._btn(self.bottom, "Finish", self._finish,
                  primary=True).pack(side="right", padx=16, pady=10)

    def _opt_row(self, parent, title, subtitle, var, row):
        row_f = tk.Frame(parent, bg=BG2)
        row_f.grid(row=row, column=0, sticky="ew", pady=6)
        parent.columnconfigure(0, weight=1)

        txt_f = tk.Frame(row_f, bg=BG2)
        txt_f.pack(side="left", fill="x", expand=True)
        tk.Label(txt_f, text=title, font=FONT_B,
                 bg=BG2, fg=TEXT, anchor="w").pack(anchor="w")
        tk.Label(txt_f, text=subtitle, font=("Segoe UI", 8),
                 bg=BG2, fg=MUTED, anchor="w").pack(anchor="w")

        # Toggle
        toggle = tk.Canvas(row_f, width=44, height=22, bg=BG2,
                            highlightthickness=0, cursor="hand2")
        toggle.pack(side="right", padx=4)

        def draw(v):
            toggle.delete("all")
            bg = ACCENT2 if v else "#333333"
            toggle.create_rounded_rect = lambda *a, **kw: None
            toggle.create_oval(0, 0, 44, 22, fill=bg, outline="")
            x = 30 if v else 14
            toggle.create_oval(x-10, 2, x+10, 20, fill="white", outline="")

        def click():
            var.set(not var.get())
            draw(var.get())

        draw(var.get())
        toggle.bind("<Button-1>", lambda e: click())

    def _finish(self):
        import winreg
        exe    = sys.executable
        script = DRIVER_FILE
        cmd    = f'"{exe}" "{script}"'

        if self._startup_var.get():
            try:
                key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                    r"Software\Microsoft\Windows\CurrentVersion\Run",
                    0, winreg.KEY_SET_VALUE)
                winreg.SetValueEx(key, "GriffinPowerMate", 0, winreg.REG_SZ, cmd)
                winreg.CloseKey(key)
            except Exception as e:
                tk.messagebox.showwarning("Startup",
                    f"Could not set startup entry:\n{e}")

        if self._launch_var.get():
            subprocess.Popen([exe, script],
                             creationflags=subprocess.CREATE_NO_WINDOW
                             if hasattr(subprocess, "CREATE_NO_WINDOW") else 0)

        self.root.destroy()


if __name__ == "__main__":
    app = InstallerApp()
