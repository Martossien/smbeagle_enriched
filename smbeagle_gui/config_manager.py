import yaml
from pathlib import Path

DEFAULT_PROFILES = {
    'Audit Complete': {
        'local_path': True,
        'sizefile': True,
        'access_time': True,
        'fileattributes': True,
        'ownerfile': True,
        'fasthash': True,
        'file_signature': True
    },
    'Quick Scan': {
        'local_path': True,
        'sizefile': True,
        'access_time': True
    },
    'Forensic Deep': {
        'local_path': True,
        'sizefile': True,
        'access_time': True,
        'fileattributes': True,
        'ownerfile': True,
        'fasthash': True,
        'file_signature': True,
        'verbose': True
    }
}

class ConfigManager:
    def __init__(self, profiles_file=None):
        if profiles_file is None:
            profiles_file = Path(__file__).with_name('profiles.yaml')
        self.profiles_file = Path(profiles_file)
        if not self.profiles_file.exists():
            self.save_profiles(DEFAULT_PROFILES)

    def load_profiles(self):
        if self.profiles_file.exists():
            with open(self.profiles_file, 'r', encoding='utf-8') as f:
                profiles = yaml.safe_load(f) or {}
        else:
            profiles = {}
        return profiles

    def save_profiles(self, profiles):
        with open(self.profiles_file, 'w', encoding='utf-8') as f:
            yaml.dump(profiles, f, default_flow_style=False)

    def save_profile(self, name, config):
        profiles = self.load_profiles()
        profiles[name] = config
        self.save_profiles(profiles)

    def delete_profile(self, name):
        profiles = self.load_profiles()
        if name in profiles:
            del profiles[name]
            self.save_profiles(profiles)

    def get_profile_names(self):
        return list(self.load_profiles().keys())

    def get_profile(self, name):
        return self.load_profiles().get(name)
