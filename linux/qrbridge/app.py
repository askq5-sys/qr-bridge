"""
Окно программы (GTK 3). Использует только то, что уже есть в Linux Mint:
python3, PyGObject и GTK 3.

QR переключается сам:
  поле пустое     -> QR подключения телефона (страница для отправки текста);
  в поле текст    -> QR этого текста (для прокси-клиента или камеры);
  текст > 2953 Б  -> QR подключения, текст забирается со страницы.

Язык (RU/EN) переключается в правом верхнем углу и запоминается.
"""
import gi

gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
gi.require_version("GdkPixbuf", "2.0")
gi.require_version("Pango", "1.0")
from gi.repository import Gdk, GdkPixbuf, GLib, Gtk, Pango  # noqa: E402

from . import __version__, settings  # noqa: E402
from .i18n import LANGS, I18n, load_strings, system_lang  # noqa: E402
from .netinfo import local_ips  # noqa: E402
from .qr import DataTooLong, QrCode  # noqa: E402
from .server import BridgeServer, load_page  # noqa: E402

DEBOUNCE_MS = 120          # пауза после ввода, прежде чем перестраивать QR
CSS = b"#qr-area, #qr-area viewport { background-color: #ffffff; }"


class MainWindow(Gtk.Window):
    def __init__(self, server, i18n, prefs):
        super().__init__()
        self.server = server
        self.server.on_text = lambda t: GLib.idle_add(self.on_incoming, t)
        self.i18n = i18n
        self.prefs = prefs
        self.clip = Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD)
        self.qr = None
        self._side = 0          # сторона области под QR, пиксели
        self._pending = 0       # id отложенного обновления QR

        self.set_icon_name("qr-bridge")
        self._set_initial_size()
        self.connect("destroy", Gtk.main_quit)
        self._build()
        self.apply_language()
        self.fill_ips()

    def t(self, key, **values):
        return self.i18n.t(key, **values)

    # ------------------------------------------------------------------ интерфейс
    def _set_initial_size(self):
        height = 820
        try:
            display = Gdk.Display.get_default()
            monitor = display.get_primary_monitor() or display.get_monitor(0)
            height = min(height, monitor.get_workarea().height - 80)
        except Exception:
            pass
        self.set_default_size(560, max(480, height))

    def _build(self):
        css = Gtk.CssProvider()
        css.load_from_data(CSS)
        Gtk.StyleContext.add_provider_for_screen(
            Gdk.Screen.get_default(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

        root = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=6, border_width=10)
        self.add(root)

        header = Gtk.Box(spacing=6)
        self.lbl_text = Gtk.Label(xalign=0)
        header.pack_start(self.lbl_text, True, True, 0)
        self.lang = Gtk.ComboBoxText()
        for code in LANGS:
            self.lang.append(code, code.upper())
        self.lang.set_active_id(self.i18n.lang)
        self.lang.connect("changed", self.on_lang_changed)
        header.pack_end(self.lang, False, False, 0)
        root.pack_start(header, False, False, 0)

        self.view = Gtk.TextView(wrap_mode=Gtk.WrapMode.CHAR, monospace=True)
        self.buf = self.view.get_buffer()
        self.buf.connect("changed", self.on_changed)
        text_sw = Gtk.ScrolledWindow(shadow_type=Gtk.ShadowType.IN)
        text_sw.set_size_request(-1, 90)
        text_sw.add(self.view)
        root.pack_start(text_sw, False, True, 0)

        buttons = Gtk.Box(spacing=6)
        self.btn_paste = Gtk.Button()
        self.btn_copy = Gtk.Button()
        self.btn_clear = Gtk.Button()
        for b, handler in ((self.btn_paste, self.paste),
                           (self.btn_copy, self.copy),
                           (self.btn_clear, self.clear)):
            b.connect("clicked", lambda _b, h=handler: h())
            buttons.pack_start(b, False, False, 0)
        self.count = Gtk.Label()
        buttons.pack_end(self.count, False, False, 0)
        root.pack_start(buttons, False, False, 0)

        # QR масштабируется под окно; EXTERNAL не даёт картинке распирать окно
        self.image = Gtk.Image()
        self.area = Gtk.ScrolledWindow()
        self.area.set_policy(Gtk.PolicyType.EXTERNAL, Gtk.PolicyType.EXTERNAL)
        self.area.set_name("qr-area")
        self.area.add(self.image)
        self.area.connect("size-allocate", self.on_area_size)
        root.pack_start(self.area, True, True, 0)

        self.caption = Gtk.Label(ellipsize=Pango.EllipsizeMode.END)
        root.pack_start(self.caption, False, False, 0)

        ip_row = Gtk.Box(spacing=6)
        self.lbl_ip = Gtk.Label()
        ip_row.pack_start(self.lbl_ip, False, False, 0)
        self.ip = Gtk.ComboBoxText()
        self.ip.connect("changed", lambda _c: self.update_qr())
        ip_row.pack_start(self.ip, False, False, 0)
        self.btn_refresh = Gtk.Button()
        self.btn_refresh.connect("clicked", lambda _b: self.fill_ips())
        ip_row.pack_start(self.btn_refresh, False, False, 0)
        root.pack_start(ip_row, False, False, 0)

        self.status = Gtk.Label(xalign=0, ellipsize=Pango.EllipsizeMode.END)
        root.pack_start(self.status, False, False, 0)

    def apply_language(self):
        self.set_title("%s %s" % (self.t("app.title"), __version__))
        self.lbl_text.set_text(self.t("label.text"))
        self.btn_paste.set_label(self.t("btn.paste"))
        self.btn_copy.set_label(self.t("btn.copy"))
        self.btn_clear.set_label(self.t("btn.clear"))
        self.btn_refresh.set_label(self.t("btn.refresh"))
        self.lbl_ip.set_text(self.t("label.ip"))
        self.count.set_text(self.t("count", n=len(self.get_text())))
        self.say("")
        self.update_qr()

    def on_lang_changed(self, combo):
        code = combo.get_active_id()
        if code and code != self.i18n.lang:
            self.i18n.set_lang(code)
            self.prefs["lang"] = code
            settings.save(self.prefs)
            self.apply_language()

    # ------------------------------------------------------------------ QR
    def current_ip(self):
        return self.ip.get_active_text() or "127.0.0.1"

    def update_qr(self):
        self._pending = 0
        text = self.get_text()
        url = self.server.url(self.current_ip())
        tooltip = url
        if text:
            try:
                self.qr = QrCode.encode(text, "L")
                self.caption.set_text(self.t("caption.text", n=len(text)))
                tooltip = None
            except DataTooLong:
                self.qr = QrCode.encode(url, "M")
                self.caption.set_text(self.t("caption.toolong"))
        else:
            self.qr = QrCode.encode(url, "M")
            self.caption.set_text(self.t("caption.connect"))
        self.area.set_tooltip_text(tooltip)
        self._render()
        return False            # для GLib.timeout_add: не повторять

    def on_area_size(self, _widget, alloc):
        side = min(alloc.width, alloc.height)
        if side != self._side:
            self._side = side
            GLib.idle_add(self._render)

    def _render(self):
        if self.qr is None or self._side < 40:
            return False
        data, side = self.qr.to_rgb(self._side - 4)
        pixbuf = GdkPixbuf.Pixbuf.new_from_bytes(
            GLib.Bytes.new(data), GdkPixbuf.Colorspace.RGB, False, 8, side, side, side * 3)
        self.image.set_from_pixbuf(pixbuf)
        return False

    # ------------------------------------------------------------------ текст
    def get_text(self):
        start, end = self.buf.get_bounds()
        return self.buf.get_text(start, end, False).strip()

    def set_text(self, text):
        self.buf.set_text(text)

    def on_changed(self, _buf):
        text = self.get_text()
        self.server.text = text
        self.count.set_text(self.t("count", n=len(text)))
        if self._pending:
            GLib.source_remove(self._pending)
        self._pending = GLib.timeout_add(DEBOUNCE_MS, self.update_qr)

    def paste(self):
        text = self.clip.wait_for_text()
        if text:
            self.set_text(text)
            self.say(self.t("status.pasted"))
        else:
            self.say(self.t("status.clip_empty"), error=True)

    def copy(self):
        text = self.get_text()
        if not text:
            self.say(self.t("status.nothing"), error=True)
            return
        self.clip.set_text(text, -1)
        self.clip.store()
        self.say(self.t("status.copied", n=len(text)))

    def clear(self):
        self.set_text("")
        self.say("")

    def on_incoming(self, text):
        self.set_text(text)
        if self._pending:
            GLib.source_remove(self._pending)
        self.update_qr()
        self.copy()
        self.say(self.t("status.received", n=len(text)))
        self.present()
        return False            # однократный вызов из GLib.idle_add

    # ------------------------------------------------------------------ прочее
    def fill_ips(self):
        self.ip.remove_all()
        for ip in local_ips():
            self.ip.append_text(ip)
        self.ip.set_active(0)

    def say(self, msg, error=False):
        color = "#c62828" if error else "#2e7d32"
        self.status.set_markup('<span foreground="%s">%s</span>' % (color, GLib.markup_escape_text(msg)))


def _error_dialog(title, message):
    dlg = Gtk.MessageDialog(message_type=Gtk.MessageType.ERROR,
                            buttons=Gtk.ButtonsType.CLOSE, text=title)
    dlg.format_secondary_text(message)
    dlg.run()
    dlg.destroy()


def main():
    prefs = settings.load()
    i18n = I18n(load_strings(), prefs.get("lang") or system_lang())
    GLib.set_prgname("qr-bridge")
    GLib.set_application_name(i18n.t("app.title"))
    server = BridgeServer(load_page(), i18n)
    try:
        server.start()
    except OSError as e:
        _error_dialog(i18n.t("app.title"), str(e))
        return 1
    win = MainWindow(server, i18n, prefs)
    win.show_all()
    try:
        Gtk.main()
    finally:
        server.stop()
    return 0
