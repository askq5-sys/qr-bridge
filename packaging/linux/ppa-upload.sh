#!/bin/sh
# Загрузка исходного пакета в PPA на Launchpad: по одной сборке на каждую версию Ubuntu.
#
#   packaging/linux/ppa-upload.sh КЛЮЧ_GPG [серии...]
#
# Серии по умолчанию: noble (Ubuntu 24.04 = Linux Mint 22) и jammy (22.04 = Mint 21).
# PPA можно сменить: PPA=ppa:имя/архив packaging/linux/ppa-upload.sh ...
# Проверка без подписи и загрузки: DRY_RUN=1 packaging/linux/ppa-upload.sh - noble
set -eu

if [ $# -lt 1 ]; then
    echo "Использование: $0 КЛЮЧ_GPG [noble jammy ...]" >&2
    exit 1
fi
KEY=$1
shift
SERIES=${*:-"noble jammy"}
PPA=${PPA:-ppa:askq5-sys/qr-bridge}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
VERSION=$(sed -n 's/^__version__ = "\(.*\)"/\1/p' "$ROOT/linux/qrbridge/__init__.py")

if [ "${DRY_RUN:-}" = 1 ]; then
    SIGNER="Test <test@example.com>"
else
    # «Имя <почта>» берём из самого ключа: подпись и changelog должны совпадать
    SIGNER=$(gpg --list-keys --with-colons "$KEY" | awk -F: '/^uid/ {print $10; exit}')
    [ -n "$SIGNER" ] || { echo "Ключ $KEY не найден в gpg" >&2; exit 1; }
fi

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

for SERIES_NAME in $SERIES; do
    PKG_VERSION="${VERSION}~${SERIES_NAME}1"
    DIR="$WORK/qr-bridge-$PKG_VERSION"
    mkdir -p "$DIR"
    (cd "$ROOT" && tar --exclude=./.git --exclude=./dist --exclude='__pycache__' -cf - .) | (cd "$DIR" && tar -xf -)
    # при загрузке через сайт GitHub права на запуск теряются — возвращаем
    chmod 755 "$DIR/debian/rules" "$DIR/packaging/linux/qr-bridge" "$DIR"/packaging/linux/*.sh

    cat > "$DIR/debian/changelog" <<EOF
qr-bridge ($PKG_VERSION) $SERIES_NAME; urgency=medium

  * Release $VERSION for $SERIES_NAME.

 -- $SIGNER  $(LC_ALL=C date -R)
EOF

    echo "== $PKG_VERSION ($SERIES_NAME)"
    if [ "${DRY_RUN:-}" = 1 ]; then
        (cd "$WORK" && dpkg-source -b "qr-bridge-$PKG_VERSION")
        ls "$WORK"/qr-bridge_"$PKG_VERSION"*
    else
        (cd "$DIR" && dpkg-buildpackage -S -k"$KEY")
        dput "$PPA" "$WORK/qr-bridge_${PKG_VERSION}_source.changes"
    fi
done

echo "Готово. Сборка на Launchpad: https://launchpad.net/~${PPA#ppa:}" | sed 's#/\([^/]*\)$#/+archive/ubuntu/\1#'
