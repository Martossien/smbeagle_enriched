import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from pathlib import Path
import re

from config_manager import ConfigManager
from execution_manager import ExecutionManager

PROGRESS_PATTERNS = {
    r'Enumerating all subdirectories': 20,
    r'Splitting large directories': 40,
    r'Enumerating files': 60,
    r'file enumeration complete': 90,
    r'AUDIT COMPLETE': 100
}

TOOLTIPS = {
    'sizefile': 'Collects the exact file size in bytes for each discovered file.\nUseful for: Data governance, storage analysis, duplicate detection.',
    'access_time': 'Captures the last access timestamp (when file was read).\nUseful for: Usage patterns, stale file identification, forensic timelines.',
    'fileattributes': 'Gathers Windows file system attributes (Hidden, ReadOnly, System, etc.).\nUseful for: Security analysis, detecting hidden files, compliance checks.',
    'ownerfile': 'Determines file ownership information (DOMAIN\\Username).\nUseful for: Access control audits, data ownership mapping, compliance.',
    'fasthash': 'Generates xxHash64 checksum of first 64KB for file identification.\nUseful for: Duplicate detection, integrity verification, malware analysis.',
    'file_signature': 'Identifies actual file type by analyzing magic bytes in file header.\nUseful for: Finding misnamed files, detecting disguised malware, file classification.'
}

class SMBeagleGUI:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title('SMBeagle_enriched GUI v4.0.1.1')
        self.root.geometry('1200x800')

        self.config_manager = ConfigManager()
        self.executor = ExecutionManager()

        self.local_scan_var = tk.BooleanVar(value=True)
        self.local_path_var = tk.StringVar()
        self.network_scan_var = tk.BooleanVar()
        self.csv_file_var = tk.StringVar(value='audit_output.csv')
        self.elasticsearch_var = tk.StringVar()
        self.verbose_var = tk.BooleanVar()

        self.metadata_options = {
            'sizefile': {'var': tk.BooleanVar(), 'icon': '\U0001F4CF', 'desc': 'Collect file sizes in bytes'},
            'access_time': {'var': tk.BooleanVar(), 'icon': '\u23F0', 'desc': 'Collect last access timestamps'},
            'fileattributes': {'var': tk.BooleanVar(), 'icon': '\U0001F3F7\uFE0F', 'desc': 'Collect system attributes'},
            'ownerfile': {'var': tk.BooleanVar(), 'icon': '\U0001F464', 'desc': 'Collect file ownership'},
            'fasthash': {'var': tk.BooleanVar(), 'icon': '\u0023\uFE0F\u20E3', 'desc': 'Generate xxHash64 checksums'},
            'file_signature': {'var': tk.BooleanVar(), 'icon': '\U0001F50D', 'desc': 'Detect file types (magic)'}
        }

        self.command_var = tk.StringVar()

        self.setup_ui()
        self.update_command_preview()

    # --- UI SETUP ---
    def setup_ui(self):
        # Frames
        frm_scan = ttk.LabelFrame(self.root, text='Scan Configuration')
        frm_scan.pack(fill='x', padx=10, pady=5)

        cb_local = ttk.Checkbutton(frm_scan, text='--local-path', variable=self.local_scan_var, command=self.update_command_preview)
        cb_local.grid(row=0, column=0, sticky='w', padx=5, pady=5)
        entry_local = ttk.Entry(frm_scan, textvariable=self.local_path_var, width=60)
        entry_local.grid(row=0, column=1, padx=5, pady=5)
        btn_browse = ttk.Button(frm_scan, text='Browse...', command=self.browse_local)
        btn_browse.grid(row=0, column=2, padx=5, pady=5)

        cb_network = ttk.Checkbutton(frm_scan, text='Network scan (Advanced)', variable=self.network_scan_var, command=self.update_command_preview)
        cb_network.grid(row=1, column=0, sticky='w', padx=5, pady=5)

        frm_meta = ttk.LabelFrame(self.root, text='Metadata Collection')
        frm_meta.pack(fill='x', padx=10, pady=5)

        # metadata grid 2x3
        row = col = 0
        for key, cfg in self.metadata_options.items():
            cb = ttk.Checkbutton(frm_meta, text=f"{cfg['icon']} --{key}", variable=cfg['var'], command=self.update_command_preview)
            cb.grid(row=row, column=col, sticky='w', padx=5, pady=5)
            self.create_tooltip(cb, TOOLTIPS.get(key, cfg['desc']))
            col += 1
            if col >= 3:
                col = 0
                row += 1

        frm_output = ttk.LabelFrame(self.root, text='Output Configuration')
        frm_output.pack(fill='x', padx=10, pady=5)

        ttk.Label(frm_output, text='CSV File:').grid(row=0, column=0, sticky='w', padx=5, pady=5)
        entry_csv = ttk.Entry(frm_output, textvariable=self.csv_file_var, width=50)
        entry_csv.grid(row=0, column=1, padx=5, pady=5)
        btn_csv = ttk.Button(frm_output, text='Browse...', command=self.browse_csv)
        btn_csv.grid(row=0, column=2, padx=5, pady=5)

        ttk.Label(frm_output, text='Elasticsearch:').grid(row=1, column=0, sticky='w', padx=5, pady=5)
        entry_es = ttk.Entry(frm_output, textvariable=self.elasticsearch_var, width=50)
        entry_es.grid(row=1, column=1, padx=5, pady=5)

        frm_exec = ttk.LabelFrame(self.root, text='Execution')
        frm_exec.pack(fill='x', padx=10, pady=5)

        self.profile_var = tk.StringVar(value='Audit Complete')
        self.profile_menu = ttk.OptionMenu(frm_exec, self.profile_var, self.profile_var.get(), *self.config_manager.get_profile_names(), command=self.load_profile)
        self.profile_menu.grid(row=0, column=0, padx=5, pady=5)
        ttk.Button(frm_exec, text='Save', command=self.save_profile).grid(row=0, column=1, padx=5, pady=5)
        ttk.Button(frm_exec, text='Delete', command=self.delete_profile).grid(row=0, column=2, padx=5, pady=5)

        ttk.Label(frm_exec, text='Command:').grid(row=1, column=0, sticky='w', padx=5, pady=5)
        self.lbl_command = ttk.Label(frm_exec, textvariable=self.command_var, relief='sunken', anchor='w')
        self.lbl_command.grid(row=1, column=1, columnspan=2, sticky='we', padx=5, pady=5)

        frm_exec.columnconfigure(1, weight=1)

        ttk.Button(frm_exec, text='\u1F680 START AUDIT', command=self.execute_scan).grid(row=2, column=0, columnspan=3, pady=10)

        frm_progress = ttk.LabelFrame(self.root, text='Progress & Logs')
        frm_progress.pack(fill='both', expand=True, padx=10, pady=5)

        self.progress_var = tk.IntVar()
        self.lbl_status = ttk.Label(frm_progress, text='Status: Ready')
        self.lbl_status.pack(anchor='w', padx=5, pady=2)
        self.progress = ttk.Progressbar(frm_progress, variable=self.progress_var, maximum=100)
        self.progress.pack(fill='x', padx=5, pady=2)

        self.txt_log = tk.Text(frm_progress, height=15)
        self.txt_log.pack(fill='both', expand=True, padx=5, pady=5)

        # Bind variable traces to command preview
        vars_to_trace = [self.local_scan_var, self.local_path_var, self.network_scan_var,
                         self.csv_file_var, self.elasticsearch_var, self.verbose_var]
        for cfg in self.metadata_options.values():
            vars_to_trace.append(cfg['var'])
        for var in vars_to_trace:
            var.trace_add('write', lambda *args: self.update_command_preview())

    # --- Browsing dialogs ---
    def browse_local(self):
        path = filedialog.askdirectory()
        if path:
            self.local_path_var.set(path)

    def browse_csv(self):
        file = filedialog.asksaveasfilename(defaultextension='.csv', filetypes=[('CSV','*.csv')])
        if file:
            self.csv_file_var.set(file)

    # --- Profile management ---
    def load_profile(self, name=None, *_):
        if name is None:
            name = self.profile_var.get()
        profile = self.config_manager.get_profile(name)
        if not profile:
            return
        self.apply_profile(profile)

    def apply_profile(self, profile):
        self.local_scan_var.set(profile.get('local_path', False))
        for key in self.metadata_options:
            self.metadata_options[key]['var'].set(profile.get(key, False))
        self.verbose_var.set(profile.get('verbose', False))
        self.update_command_preview()

    def save_profile(self):
        name = self.profile_var.get()
        config = self.collect_profile()
        self.config_manager.save_profile(name, config)
        self.refresh_profiles()
        messagebox.showinfo('Profile Saved', f'Profile "{name}" saved.')

    def delete_profile(self):
        name = self.profile_var.get()
        self.config_manager.delete_profile(name)
        self.refresh_profiles()
        messagebox.showinfo('Profile Deleted', f'Profile "{name}" deleted.')

    def collect_profile(self):
        profile = {
            'local_path': self.local_scan_var.get(),
            'verbose': self.verbose_var.get()
        }
        for key in self.metadata_options:
            profile[key] = self.metadata_options[key]['var'].get()
        return profile

    def refresh_profiles(self):
        menu = self.profile_menu['menu']
        menu.delete(0, 'end')
        for name in self.config_manager.get_profile_names():
            menu.add_command(label=name, command=lambda n=name: self.profile_var.set(n))
        if self.profile_var.get() not in self.config_manager.get_profile_names():
            self.profile_var.set('')

    # --- Command build ---
    def build_command(self):
        parts = ['SMBeagle']
        if self.local_scan_var.get() and self.local_path_var.get():
            path = self.local_path_var.get()
            parts.extend(['--local-path', f'"{path}"'])
        if self.network_scan_var.get():
            parts.append('--network')
        for key, cfg in self.metadata_options.items():
            if cfg['var'].get():
                parts.append(f'--{key}')
        if self.csv_file_var.get():
            parts.extend(['-c', f'"{self.csv_file_var.get()}"'])
        if self.elasticsearch_var.get():
            parts.extend(['-e', self.elasticsearch_var.get()])
        if self.verbose_var.get():
            parts.append('-v')
        return ' '.join(parts)

    def update_command_preview(self):
        cmd = self.build_command()
        display = cmd if len(cmd) <= 80 else cmd[:77] + '...'
        self.command_var.set(display)
        self.lbl_command.bind('<Enter>', lambda e, full=cmd: self.show_full_command(full, e))
        self.lbl_command.bind('<Leave>', lambda e: self.hide_tooltip())

    def show_full_command(self, cmd, event):
        self.create_tooltip(self.lbl_command, cmd, event)

    def hide_tooltip(self):
        if hasattr(self.lbl_command, 'tooltip'):
            self.lbl_command.tooltip.destroy()
            delattr(self.lbl_command, 'tooltip')

    # --- Execution ---
    def execute_scan(self):
        cmd = self.build_command()
        self.progress_var.set(0)
        self.lbl_status.config(text='Starting...')
        self.txt_log.delete(1.0, tk.END)
        self.executor.run(cmd, on_line=self.on_process_line, on_complete=self.on_process_complete)

    def on_process_line(self, line):
        self.txt_log.after(0, lambda: self.append_log(line))
        for pattern, perc in PROGRESS_PATTERNS.items():
            if re.search(pattern, line, re.IGNORECASE):
                self.progress_var.set(perc)
                self.lbl_status.config(text=line)
                break

    def on_process_complete(self):
        self.txt_log.after(0, lambda: self.append_log('Process finished'))
        self.progress_var.set(100)
        self.lbl_status.config(text='Completed')

    def append_log(self, text):
        self.txt_log.insert(tk.END, text + '\n')
        self.txt_log.see(tk.END)

    # --- Tooltip helper ---
    def create_tooltip(self, widget, text, event=None):
        def show(e):
            x = e.x_root + 10
            y = e.y_root + 10
            tooltip = tk.Toplevel(widget)
            tooltip.wm_overrideredirect(True)
            tooltip.wm_geometry(f"+{x}+{y}")
            label = tk.Label(tooltip, text=text, background='lightyellow', font=('Arial', 9), wraplength=300)
            label.pack()
            widget.tooltip = tooltip
        def hide(_):
            if hasattr(widget, 'tooltip'):
                widget.tooltip.destroy()
                delattr(widget, 'tooltip')
        if event:
            show(event)
        widget.bind('<Enter>', show)
        widget.bind('<Leave>', hide)

    # --- Main loop ---
    def run(self):
        self.root.mainloop()

if __name__ == '__main__':
    gui = SMBeagleGUI()
    gui.run()
