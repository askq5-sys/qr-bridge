# QR Bridge

**English** | [Русский](README.ru.md)

Send text between your computer and your phone with a QR code. Built for proxy keys
(`vless://…`, `ss://…` and similar), but works with any text.

- **Computer → phone.** Paste text into the field and the window shows it as a QR code right away.
  Scan it in your proxy app ("Import from QR" in v2rayNG, Hiddify, NekoBox) or with the camera.
- **Phone → computer.** When the field is empty, the window shows a connection QR code.
  Scan it with your phone, a page opens: paste your text and tap "Send to computer".
  The text appears in the window and is copied to the clipboard.
- If the text is too long for a single QR code (over 2953 bytes), the window shows the
  connection QR code, and you pick up the text on the phone with "Get from computer".

Nothing to install on the phone: a camera and a browser are enough.

The interface language (English / Russian) is switched in the top-right corner of the window
and remembered. The phone page opens in the same language. On first launch the language
follows the system.

## Download

Get the latest version from the [Releases](https://github.com/askq5-sys/qr-bridge/releases/latest) page.

### Linux Mint / Ubuntu / Debian

Download `qr-bridge_*.deb` and open it with a double click, or run:

```bash
sudo apt install ./qr-bridge_1.2.0_all.deb
```

It only needs `python3` and GTK 3 (`python3-gi`, `gir1.2-gtk-3.0`), which come with Linux Mint.

Run without installing: `python3 linux/qr_bridge.py`.

### Windows 10 / 11

Download `QR-bridge.exe` and run it. Nothing to install: the program uses
.NET Framework 4, which is built into Windows.

Windows may show "Windows protected your PC" because the file is not digitally signed.
Click "More info" → "Run anyway".

On first launch Windows asks about network access: click "Allow". If your Wi-Fi network is
marked as "Public", allow it there too or switch the network to "Private".

To build the exe yourself, double-click `windows/build.bat`. It uses the C# compiler
that ships with every Windows 10/11.

## Security

- The server runs only while the window is open and listens only on your local network.
- The QR link contains a random access code that changes on every launch. Without it the
  server answers "Access denied", so other devices on the network cannot read your text.
- The connection is plain HTTP inside your Wi-Fi network, without encryption. Do not use
  the program on public or untrusted networks.

## Troubleshooting

- **The phone cannot open the page.** Make sure both devices are on the same Wi-Fi network and
  pick an address like `192.168.x.x` under "Computer address". With a VPN in TUN mode the list
  may contain the tunnel address.
- **Firewall on Linux:** `sudo ufw allow 8765/tcp`.
- **A large QR code does not scan.** Maximize the window and turn up the screen brightness.
  If that does not help, send the text through the phone page instead.

## Project layout

```
shared/            shared by both versions
  page.html        phone page (texts and access code are filled in at runtime)
  strings.ini      all interface texts in Russian and English
  qr-bridge.svg    icon
linux/
  qr_bridge.py     entry point
  qrbridge/
    qr.py          QR encoder (QrCode), no dependencies
    server.py      HTTP server (BridgeServer)
    netinfo.py     the computer's network addresses
    i18n.py        texts and language selection (I18n)
    settings.py    user settings (~/.config/qr-bridge)
    app.py         GTK 3 window (MainWindow)
windows/
  build.bat        builds QR-bridge.exe with the built-in csc.exe
  qr-bridge.ico
  src/             QrCode.cs, BridgeServer.cs, NetInfo.cs, I18n.cs, Settings.cs,
                   MainForm.cs, Program.cs
packaging/linux/   build-deb.sh, qr-bridge.desktop
tests/             python3 -m unittest discover -s tests
```

Both versions share the same structure: QR encoder, server, network addresses and window
are separate classes. The phone page and interface texts are shared; on Windows they are
embedded into the exe at build time. To add a language, add a section to
`shared/strings.ini` and the language code to `LANGS` (i18n.py) and `Langs` (I18n.cs).

### Building the .deb

```bash
MAINTAINER="Name <email>" packaging/linux/build-deb.sh   # output goes to dist/
```

## License

MIT, see [LICENSE](LICENSE).
