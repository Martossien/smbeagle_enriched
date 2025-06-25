import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from smbeagle_gui.smbeagle_gui import SMBeagleGUI
from smbeagle_gui.config_manager import ConfigManager


class DummyVar:
    def __init__(self, value=None):
        self._value = value

    def get(self):
        return self._value

    def set(self, value):
        self._value = value


def create_gui_stub():
    gui = SMBeagleGUI.__new__(SMBeagleGUI)
    gui.local_scan_var = DummyVar(False)
    gui.local_path_var = DummyVar("")
    gui.network_scan_var = DummyVar(False)
    gui.csv_file_var = DummyVar("")
    gui.elasticsearch_var = DummyVar("")
    gui.verbose_var = DummyVar(False)
    gui.metadata_options = {
        "sizefile": {"var": DummyVar(False)},
        "access_time": {"var": DummyVar(False)},
        "fileattributes": {"var": DummyVar(False)},
        "ownerfile": {"var": DummyVar(False)},
        "fasthash": {"var": DummyVar(False)},
        "file_signature": {"var": DummyVar(False)},
    }
    gui.cli_option_mapping = {"file_signature": "file-signature"}
    return gui


def test_file_signature_mapping():
    gui = create_gui_stub()
    gui.metadata_options["file_signature"]["var"].set(True)
    cmd = SMBeagleGUI.build_command(gui)
    assert "--file-signature" in cmd
    assert "--file_signature" not in cmd


def test_profile_loads_file_signature():
    cm = ConfigManager()
    profile = cm.get_profile("Audit Complete")
    gui = create_gui_stub()
    for key in gui.metadata_options:
        gui.metadata_options[key]["var"].set(profile.get(key, False))
    cmd = SMBeagleGUI.build_command(gui)
    assert "--file-signature" in cmd
