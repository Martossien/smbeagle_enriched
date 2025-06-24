import threading
import subprocess

class ExecutionManager:
    def __init__(self):
        self.thread = None
        self.process = None

    def run(self, command, on_line=None, on_complete=None):
        """Run command asynchronously."""
        def target():
            try:
                self.process = subprocess.Popen(
                    command,
                    shell=True,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    bufsize=1,
                    universal_newlines=True
                )
                for line in iter(self.process.stdout.readline, ''):
                    if on_line:
                        on_line(line.rstrip())
                self.process.wait()
            except Exception as exc:
                if on_line:
                    on_line(f"Error: {exc}")
            finally:
                if on_complete:
                    on_complete()
                self.process = None

        self.thread = threading.Thread(target=target, daemon=True)
        self.thread.start()

    def terminate(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
