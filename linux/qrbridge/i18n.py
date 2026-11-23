"""Тексты интерфейса на русском и английском (shared/strings.ini)."""
import locale
import os

__all__ = ["I18n", "load_strings", "parse_ini", "system_lang", "LANGS"]

LANGS = ("ru", "en")
FALLBACK = "en"


def parse_ini(text):
    """[язык] + строки «ключ = значение» -> {язык: {ключ: значение}}."""
    table, section = {}, None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith(("#", ";")):
            continue
        if line.startswith("[") and line.endswith("]"):
            section = table.setdefault(line[1:-1].strip(), {})
        elif "=" in line and section is not None:
            key, value = line.split("=", 1)
            section[key.strip()] = value.strip()
    return table


def load_strings():
    """strings.ini лежит рядом с пакетом (в .deb) или в shared/ (в исходниках)."""
    here = os.path.dirname(os.path.abspath(__file__))
    for path in (os.path.join(here, "strings.ini"),
                 os.path.join(here, "..", "..", "shared", "strings.ini")):
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                return parse_ini(f.read())
    raise FileNotFoundError("не найден strings.ini")


def system_lang():
    for var in ("LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG"):
        value = os.environ.get(var, "")
        if value:
            return "ru" if value.lower().startswith("ru") else FALLBACK
    try:
        loc = locale.getlocale()[0] or ""
    except ValueError:
        loc = ""
    return "ru" if loc.lower().startswith("ru") else FALLBACK


class I18n:
    def __init__(self, table, lang):
        self._table = table
        self.lang = FALLBACK
        self.set_lang(lang)

    def set_lang(self, lang):
        if lang in self._table:
            self.lang = lang

    def t(self, key, **values):
        text = self._table.get(self.lang, {}).get(key) \
            or self._table.get(FALLBACK, {}).get(key) or key
        for name, value in values.items():
            text = text.replace("{%s}" % name, str(value))
        return text
