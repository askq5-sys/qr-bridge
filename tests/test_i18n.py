"""Проверка файла текстов: одинаковые ключи в обоих языках и безопасные символы."""
import os
import re
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "linux"))
from qrbridge.i18n import I18n, LANGS, load_strings  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..")


class I18nTest(unittest.TestCase):
    def setUp(self):
        self.table = load_strings()

    def test_same_keys(self):
        self.assertEqual(set(self.table), set(LANGS))
        self.assertEqual(set(self.table["ru"]), set(self.table["en"]))

    def test_safe_chars(self):
        for lang, strings in self.table.items():
            for key, value in strings.items():
                self.assertFalse(set(value) & set('"\\<>&'), "%s.%s" % (lang, key))

    def test_page_keys_exist(self):
        with open(os.path.join(ROOT, "shared", "page.html"), encoding="utf-8") as f:
            keys = set(re.findall(r"\{\{([\w.]+)\}\}", f.read())) - {"lang"}
        self.assertTrue(keys)
        self.assertLessEqual(keys, set(self.table["en"]))

    def test_code_keys_exist(self):
        used = set()
        for path in (os.path.join(ROOT, "linux", "qrbridge", "app.py"),
                     os.path.join(ROOT, "linux", "qrbridge", "server.py"),
                     os.path.join(ROOT, "windows", "src", "MainForm.cs"),
                     os.path.join(ROOT, "windows", "src", "BridgeServer.cs"),
                     os.path.join(ROOT, "windows", "src", "Program.cs")):
            with open(path, encoding="utf-8-sig") as f:
                used |= set(re.findall(r"""\bt\(\s*["']([a-z_]+\.[a-z_.]+|count)["']""", f.read(), re.I))
        self.assertTrue(used)
        self.assertLessEqual(used, set(self.table["en"]), used - set(self.table["en"]))

    def test_substitution(self):
        i = I18n(self.table, "en")
        self.assertEqual(i.t("count", n=5), "5 chars")
        i.set_lang("ru")
        self.assertEqual(i.t("count", n=5), "5 символов")
        i.set_lang("de")            # неизвестный язык игнорируется
        self.assertEqual(i.lang, "ru")


if __name__ == "__main__":
    unittest.main()
