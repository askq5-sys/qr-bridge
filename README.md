# QR-мост / QR Bridge

**English:** send text between your computer and phone via QR code — e.g. proxy keys
(`vless://…`, `ss://…`). Paste text and the window shows it as a QR code; leave the field
empty and scan the QR with your phone to send text back. Linux (GTK 3) and Windows (.NET 4),
no extra libraries. Downloads: [Releases](https://github.com/askq5-sys/qr-bridge/releases/latest).
The interface language (RU/EN) is switched in the top-right corner.

---

Передача текста между компьютером и телефоном через QR-код. Сделано для ключей
прокси (`vless://…`, `ss://…` и т. п.), но подходит для любого текста.

- **Компьютер → телефон.** Вставьте текст в поле, и окно сразу покажет его QR-кодом.
  Сканируйте в прокси-клиенте («Импорт из QR» в v2rayNG, Hiddify, NekoBox) или камерой.
- **Телефон → компьютер.** При пустом поле окно показывает QR подключения.
  Телефон сканирует его, открывается страница: вставьте текст, нажмите «Отправить на компьютер».
  Текст появится в окне и скопируется в буфер обмена.
- Если текст длиннее, чем помещается в один QR (2953 байта), окно показывает QR подключения,
  а на телефоне текст забирается кнопкой «Забрать с компьютера».

На телефон ничего ставить не нужно, хватит камеры и браузера.

Язык интерфейса (RU/EN) переключается в правом верхнем углу окна и запоминается.
Страница на телефоне открывается на том же языке. При первом запуске язык берётся из системы.

## Установка

### Linux Mint / Ubuntu / Debian

Скачайте `qr-bridge_*.deb` со страницы [Releases](https://github.com/askq5-sys/qr-bridge/releases/latest) и откройте двойным кликом или выполните:

```bash
sudo apt install ./qr-bridge_1.2.0_all.deb
```

Нужны только `python3` и GTK 3 (`python3-gi`, `gir1.2-gtk-3.0`), которые уже есть в Linux Mint.

Запуск без установки: `python3 linux/qr_bridge.py`.

### Windows 10 / 11

Скачайте `QR-bridge.exe` со страницы [Releases](https://github.com/askq5-sys/qr-bridge/releases/latest) и запустите. Устанавливать ничего не нужно:
программа использует .NET Framework 4, встроенный в Windows.

Собрать exe самостоятельно: дважды щёлкните `windows/build.bat`. Он использует
компилятор C#, который есть в каждой Windows 10/11.

При первом запуске Windows спросит про доступ к сети: нажмите «Разрешить». Если ваша
Wi-Fi-сеть помечена как «Общедоступная», разрешите доступ и для неё или переключите сеть
на «Частную».

## Безопасность

- Сервер работает только пока открыто окно и слушает только локальную сеть.
- В ссылке QR есть случайный код доступа, который меняется при каждом запуске.
  Без него сервер отвечает «Нет доступа», поэтому другие устройства в сети не смогут забрать текст.
- Соединение идёт по HTTP без шифрования, внутри вашей Wi-Fi-сети. Не пользуйтесь
  программой в чужих и публичных сетях.

## Если что-то не работает

- **Телефон не открывает страницу.** Проверьте, что устройства в одной Wi-Fi-сети, и выберите
  в списке «Адрес компьютера в сети» адрес вида `192.168.x.x`. При включённом VPN в режиме TUN
  в списке может оказаться адрес туннеля.
- **Файрвол в Linux:** `sudo ufw allow 8765/tcp`.
- **Большой QR не читается.** Разверните окно и прибавьте яркость экрана.
  Если не помогло, отправьте текст через страницу.

## Устройство проекта

```
shared/            общее для обеих версий
  page.html        страница для телефона (тексты и код доступа подставляются при запуске)
  strings.ini      все тексты интерфейса на русском и английском
  qr-bridge.svg    иконка
linux/
  qr_bridge.py     точка запуска
  qrbridge/
    qr.py          генератор QR (QrCode), без зависимостей
    server.py      HTTP-сервер (BridgeServer)
    netinfo.py     адреса компьютера в сети
    i18n.py        тексты и выбор языка (I18n)
    settings.py    сохранение настроек (~/.config/qr-bridge)
    app.py         окно GTK 3 (MainWindow)
windows/
  build.bat        сборка QR-bridge.exe встроенным csc.exe
  qr-bridge.ico
  src/             QrCode.cs, BridgeServer.cs, NetInfo.cs, I18n.cs, Settings.cs,
                   MainForm.cs, Program.cs
packaging/linux/   build-deb.sh, qr-bridge.desktop
tests/             python3 -m unittest discover -s tests
```

Обе версии устроены одинаково: генератор QR, сервер, адреса сети и окно разнесены
по отдельным классам. Страница для телефона и тексты интерфейса общие для обеих версий:
в Windows они встраиваются в exe при сборке. Чтобы добавить язык, допишите раздел
в `shared/strings.ini` и код языка в `LANGS` (i18n.py) и `Langs` (I18n.cs).

### Сборка .deb

```bash
MAINTAINER="Имя <почта>" packaging/linux/build-deb.sh   # результат в dist/
```

## Лицензия

MIT, см. [LICENSE](LICENSE).
