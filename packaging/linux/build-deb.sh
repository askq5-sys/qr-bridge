#!/bin/sh
# Сборка .deb для Linux Mint / Ubuntu / Debian.
# Использование: packaging/linux/build-deb.sh   (результат в dist/)
# Мейнтейнера можно задать: MAINTAINER="Имя <почта>" packaging/linux/build-deb.sh
set -eu
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=$(sed -n 's/^__version__ = "\(.*\)"/\1/p' "$ROOT/linux/qrbridge/__init__.py")
MAINTAINER=${MAINTAINER:-"askq5-sys <askq5-sys@users.noreply.github.com>"}
PKG="qr-bridge_${VERSION}_all"
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
D="$STAGE/$PKG"

install -d "$D/DEBIAN" "$D/usr/bin" "$D/usr/lib/qr-bridge/qrbridge" \
           "$D/usr/share/applications" "$D/usr/share/icons/hicolor/scalable/apps" \
           "$D/usr/share/doc/qr-bridge"
install -m 644 "$ROOT"/linux/qrbridge/*.py "$D/usr/lib/qr-bridge/qrbridge/"
install -m 644 "$ROOT/shared/page.html" "$ROOT/shared/strings.ini" "$D/usr/lib/qr-bridge/qrbridge/"
install -m 755 "$ROOT/linux/qr_bridge.py" "$D/usr/lib/qr-bridge/qr_bridge.py"
install -m 644 "$ROOT/packaging/linux/qr-bridge.desktop" "$D/usr/share/applications/"
install -m 644 "$ROOT/shared/qr-bridge.svg" "$D/usr/share/icons/hicolor/scalable/apps/"
install -m 644 "$ROOT/LICENSE" "$D/usr/share/doc/qr-bridge/copyright"
install -m 755 "$ROOT/packaging/linux/qr-bridge" "$D/usr/bin/qr-bridge"

cat > "$D/DEBIAN/control" <<CTRL
Package: qr-bridge
Version: $VERSION
Section: utils
Priority: optional
Architecture: all
Depends: python3 (>= 3.8), python3-gi, gir1.2-gtk-3.0
Installed-Size: $(du -sk "$D/usr" | cut -f1)
Maintainer: $MAINTAINER
Homepage: https://github.com/askq5-sys/qr-bridge
Description: Передача текста между компьютером и телефоном через QR
 Показывает текст (например, ключ прокси) QR-кодом для сканирования
 телефоном и принимает текст с телефона через веб-страницу в локальной
 сети. Использует только python3 и GTK 3, которые уже есть в Linux Mint.
CTRL

mkdir -p "$ROOT/dist"
dpkg-deb --root-owner-group -Zxz --build "$D" "$ROOT/dist/$PKG.deb" >/dev/null
echo "dist/$PKG.deb"
