# SMBeagle_enriched GUI

This directory provides a simple tkinter-based graphical interface for the `SMBeagle` file audit tool. The GUI allows users to build commands, manage scan profiles and run the binary while viewing real-time output.

## Usage

1. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
2. Ensure `SMBeagle.exe` (or `SMBeagle` on Linux) is available in the PATH or copy it into this directory.
3. Launch the application:
   ```bash
   python smbeagle_gui.py
   ```

The GUI currently supports profile saving in `profiles.yaml` and displays the generated command before execution.
