"""
Маленький HTTP-сервер в локальной сети: отдаёт телефону страницу и
принимает/отдаёт текст. Без кода доступа (меняется при каждом запуске)
сервер отвечает 403.
"""
import http.server
import os
import re
import secrets
import threading
import urllib.parse

__all__ = ["BridgeServer", "load_page"]

MAX_BODY = 200_000
_PLACEHOLDER = re.compile(r"\{\{([\w.]+)\}\}")


def load_page():
    """page.html лежит рядом с пакетом (в .deb) или в shared/ (в исходниках)."""
    here = os.path.dirname(os.path.abspath(__file__))
    for path in (os.path.join(here, "page.html"),
                 os.path.join(here, "..", "..", "shared", "page.html")):
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                return f.read()
    raise FileNotFoundError("не найден page.html")


class BridgeServer:
    """Состояние моста: код доступа, текущий текст компьютера, обработчик входящих.
    Страница и ответы сервера — на языке, выбранном в окне (i18n.lang)."""

    def __init__(self, page_template, i18n, on_text=None):
        self.token = secrets.token_urlsafe(9)
        self.port = None
        self.i18n = i18n
        self._template = page_template
        self.on_text = on_text             # вызывается из потока сервера!
        self._text = ""
        self._lock = threading.Lock()
        self._httpd = None

    # текст, который телефон может забрать
    @property
    def text(self):
        with self._lock:
            return self._text

    @text.setter
    def text(self, value):
        with self._lock:
            self._text = value

    def url(self, ip):
        return "http://%s:%s/?t=%s" % (ip, self.port, self.token)

    def start(self, base_port=8765, attempts=20):
        for port in range(base_port, base_port + attempts):
            try:
                httpd = _HttpServer(("0.0.0.0", port), _Handler)
            except OSError:
                continue
            httpd.bridge = self
            self._httpd, self.port = httpd, port
            threading.Thread(target=httpd.serve_forever, daemon=True).start()
            return port
        raise OSError(self.i18n.t("error.port", a=base_port, b=base_port + attempts - 1))

    def stop(self):
        if self._httpd is not None:
            self._httpd.shutdown()
            self._httpd.server_close()
            self._httpd = None

    # --- вызывается обработчиком запросов ---
    def render_page(self):
        i18n = self.i18n
        html = _PLACEHOLDER.sub(
            lambda m: i18n.lang if m.group(1) == "lang" else i18n.t(m.group(1)), self._template)
        return html.replace("%TOKEN%", self.token)

    def _check(self, query):
        t = urllib.parse.parse_qs(query).get("t", [""])[0]
        return secrets.compare_digest(t, self.token)

    def _received(self, text):
        self.text = text
        if self.on_text is not None:
            self.on_text(text)


class _HttpServer(http.server.ThreadingHTTPServer):
    daemon_threads = True
    bridge = None


class _Handler(http.server.BaseHTTPRequestHandler):
    PLAIN = "text/plain; charset=utf-8"

    def _route(self):
        u = urllib.parse.urlparse(self.path)
        return u.path, self.server.bridge._check(u.query)

    def _send(self, code, body, ctype=PLAIN):
        data = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        bridge = self.server.bridge
        path, ok = self._route()
        if not ok:
            return self._send(403, bridge.i18n.t("http.forbidden"))
        if path == "/":
            return self._send(200, bridge.render_page(), "text/html; charset=utf-8")
        if path == "/api/text":
            return self._send(200, bridge.text)
        self._send(404, bridge.i18n.t("http.notfound"))

    def do_POST(self):
        bridge = self.server.bridge
        path, ok = self._route()
        if not ok:
            return self._send(403, bridge.i18n.t("http.forbidden"))
        if path != "/api/text":
            return self._send(404, bridge.i18n.t("http.notfound"))
        n = int(self.headers.get("Content-Length") or 0)
        if n < 0 or n > MAX_BODY:
            return self._send(413, bridge.i18n.t("http.toolarge"))
        bridge._received(self.rfile.read(n).decode("utf-8", "replace"))
        self._send(200, "ok")

    def log_message(self, *args):
        pass
