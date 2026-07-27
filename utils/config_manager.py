import json
from pathlib import Path


class ConfigManager:
    LANGUAGES = {"en": "English", "es": "Español", "ko": "한국어"}

    def __init__(self):
        self.base = Path(__file__).parent.parent
        self.trans_dir = self.base / "data" / "translations"
        self.config_file = self.base / "data" / "config.json"
        self.config = {"language": "en"}
        self.translations = {}
        self.lang = "en"
        self._load_config()
        self._load_translations()

    def _load_config(self):
        try:
            if self.config_file.exists():
                with open(self.config_file, "r", encoding="utf-8") as f:
                    self.config.update(json.load(f))
        except:
            pass
        self.lang = self.config.get("language", "en")

    def _load_translations(self):
        for lang in self.LANGUAGES:
            path = self.trans_dir / f"{lang}.json"
            if path.exists():
                with open(path, "r", encoding="utf-8") as f:
                    self.translations[lang] = json.load(f)

    def t(self, key: str, **kwargs):
        keys = key.split(".")
        data = self.translations.get(self.lang, {})
        for k in keys:
            if isinstance(data, dict):
                data = data.get(k, key)
            else:
                return key
        if isinstance(data, str) and kwargs:
            return data.format(**kwargs)
        return data if isinstance(data, str) else key

    def set_language(self, lang: str):
        if lang in self.LANGUAGES:
            self.lang = lang
            self.config["language"] = lang
            self.config_file.parent.mkdir(exist_ok=True)
            with open(self.config_file, "w", encoding="utf-8") as f:
                json.dump(self.config, f, indent=2)
            return True
        return False

    def get_language_name(self, lang=None):
        return self.LANGUAGES.get(lang or self.lang, "Unknown")


_cfg = None


def get_config():
    global _cfg
    if _cfg is None:
        _cfg = ConfigManager()
    return _cfg
