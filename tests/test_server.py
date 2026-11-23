"""Тесты веб-сервера (только стандартная библиотека)."""
import os
import sys
import unittest
import urllib.error
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "linux"))
from qrbridge.i18n import I18n, load_strings  # noqa: E402
from qrbridge.server import BridgeServer, load_page  # noqa: E402


class ServerTest(unittest.TestCase):
    def setUp(self):
        self.received = []
        self.i18n = I18n(load_strings(), "ru")
        self.srv = BridgeServer(load_page(), self.i18n, self.received.append)
        self.srv.start()
        self.base = "http://127.0.0.1:%d" % self.srv.port

    def tearDown(self):
        self.srv.stop()

    def get(self, path):
        return urllib.request.urlopen(self.base + path).read().decode()

    def test_no_token(self):
        for path in ("/", "/api/text", "/?t=wrong"):
            with self.assertRaises(urllib.error.HTTPError) as cm:
                self.get(path)
            self.assertEqual(cm.exception.code, 403)

    def test_page_has_token(self):
        page = self.get("/?t=" + self.srv.token)
        self.assertIn(self.srv.token, page)
        self.assertNotIn("%TOKEN%", page)

    def test_send_and_get(self):
        text = "vless://пример-ключа"
        req = urllib.request.Request(self.base + "/api/text?t=" + self.srv.token,
                                     data=text.encode("utf-8"), method="POST")
        urllib.request.urlopen(req)
        self.assertEqual(self.received, [text])
        self.assertEqual(self.get("/api/text?t=" + self.srv.token), text)

    def test_page_language(self):
        url = "/?t=" + self.srv.token
        self.assertIn("Отправить на компьютер", self.get(url))
        self.i18n.set_lang("en")
        page = self.get(url)
        self.assertIn("Send to computer", page)
        self.assertIn('lang="en"', page)
        self.assertNotIn("{{", page)

    def test_pc_text(self):
        self.srv.text = "с компьютера"
        self.assertEqual(self.get("/api/text?t=" + self.srv.token), "с компьютера")


if __name__ == "__main__":
    unittest.main()
