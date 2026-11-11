<p align="center">
  <img src="Assets/app.png" alt="Иконка SplateVPN для Linux" width="110" height="110" />
</p>

<h1 align="center">SplateVPN — VPN-клиент для Linux на Xray</h1>

<p align="center">
  VLESS / Reality, VMess, Trojan, Shadowsocks и Hysteria 2.<br />
  Системный прокси, TUN, подписки, автоматический выбор сервера и Tor.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Linux-x86__64-blue?style=flat-square" alt="Linux x86_64" />
  <img src="https://img.shields.io/badge/App-2.14.4-orange?style=flat-square" alt="Версия 2.14.4" />
  <img src="https://img.shields.io/badge/.NET-8-purple?style=flat-square" alt=".NET 8" />
  <img src="https://img.shields.io/badge/UI-Photino%20%2B%20WebKitGTK-blueviolet?style=flat-square" alt="Photino и WebKitGTK" />
</p>

<p align="center">
  <a href="https://github.com/splatedk-web/splatevpn-xray-client-linux/releases/latest">Скачать для Linux</a> ·
  <a href="#быстрый-старт">Быстрый старт</a> ·
  <a href="#другие-платформы">Другие платформы</a> ·
  <a href="https://github.com/splatedk-web/splatevpn-xray-client-linux/issues">Сообщить об ошибке</a>
</p>

## Новое в 2.14.4

Быстрый выбор использует свежие Real-замеры. Балансировщик поддерживает проверенные резервы. Исправлен запуск Xray в режиме Tor и добавлены зимние фоны из Android. [Полный список изменений](https://github.com/splatedk-web/splatevpn-xray-client-linux/releases/tag/v2.14.4)

## О приложении

Графический клиент Xray для Linux x86_64. Интерфейс работает на Photino и WebKitGTK. Клиент поддерживает системный прокси, TUN, подписки, профили и маршрутизацию. В Linux-ветку также перенесены возможности SplateVPN для Android, описанные ниже.

## Быстрый старт

Скачайте пакет `amd64.deb` из [GitHub Releases](https://github.com/splatedk-web/splatevpn-xray-client-linux/releases/latest). Из папки загрузок установите пакет, подставив его имя.

```sh
sudo apt install ./splatevpn_2.14.4-1_amd64.deb
/opt/splatevpn/SplateClient
```

Целевые системы: Ubuntu 24.04+, Linux Mint 22 и Debian 13 с графическим рабочим столом. Требуются WebKitGTK 4.1, glibc 2.38+ и libstdc++ с GLIBCXX_3.4.31. В пакете есть .NET и Xray. Графическое приложение запускается обычным пользователем, а сетевой помощник запрашивает права через polkit.

Версия 2.14.4 доступна в виде готового пакета. Установите его поверх предыдущей версии, чтобы сохранить настройки и подписки.

### Запуск в Windows через WSL

Нужны WSL 2, Ubuntu и поддержка графических приложений WSLg. Проверьте дистрибутивы командой `wsl -l -v`. Установите пакет **внутри Ubuntu**, затем запустите из PowerShell.

```powershell
wsl -d Ubuntu --exec /opt/splatevpn/SplateClient
```

Для первого подключения выберите «Системный прокси». Он действует в среде Linux. Приложения Windows используют собственные настройки прокси. Доступность TUN, polkit и раздачи зависит от конфигурации дистрибутива и сети WSL.

## Возможности Linux 2.14.4

- **Серверы и подписки.** Встроенные публичные источники можно восстановить из панели управления подписками. Быстрый выбор показывает свежие результаты Real-проверок. Автовыбор учитывает приоритет WTPN, прогревает резервные узлы и использует свежие замеры при повторном подключении.
- **WTPN.** В публичном исходном коде URL отсутствует. Если у вас есть адрес защищённой подписки от поставщика, закройте клиент и задайте `SPLATE_WTPN_URL` при первом запуске с этим источником. Клиент принимает HTTPS-адрес, отправляет HWID только по разрешённому контракту и скрывает адреса узлов в списке. Уже сохранённый защищённый источник продолжает работать.

  ```bash
  read -rs -p 'HTTPS-адрес WTPN: ' SPLATE_WTPN_URL
  printf '\n'
  export SPLATE_WTPN_URL
  /opt/splatevpn/SplateClient
  unset SPLATE_WTPN_URL
  ```

- **Маршрутизация.** Поддерживаются условия AND/OR для доменов, IP, портов, сетей и протоколов, выбор сервера для правила, поддерживаемые конструкции Clash и SingBox и SmartDNS через активный VPN. Android-правила по имени пакета в Linux недоступны.
- **Сеть.** Системный прокси интегрируется с GNOME и Cinnamon. Для TUN и Kill Switch установите `.deb` с сетевыми помощниками. Tor использует локальный Tor и Xray. Для мостов установите транспорт, который требует выбранный мост.
- **Резервные копии.** Доступны зашифрованный экспорт, импорт совместимых полей Android BackupData, ручной и автоматический WebDAV. Для сохранения учётных данных нужен доступный Secret Service. При восстановлении сохраняется локальная WTPN.
- **Рабочий стол.** Доступны XDG-автозапуск, трей и глобальная клавиша Super+Alt+V в X11. В Wayland настройте сочетание в рабочем столе для команды `/opt/splatevpn/SplateClient --toggle`.

Настройки хранятся в `${XDG_CONFIG_HOME:-$HOME/.config}/SplateVPN`. Геобазы и данные приложения хранятся в `${XDG_DATA_HOME:-$HOME/.local/share}/SplateVPN`.

## Другие платформы

- [SplateVPN для Android](https://github.com/splatedk-web/splatevpn-xray-client) · [APK и изменения](https://github.com/splatedk-web/splatevpn-xray-client/releases)
- [SplateVPN для Windows](https://github.com/splatedk-web/splatevpn-xray-client-windows) · [Установщики и изменения](https://github.com/splatedk-web/splatevpn-xray-client-windows/releases)

О проблемах с Linux сообщайте в [Issues](https://github.com/splatedk-web/splatevpn-xray-client-linux/issues). Укажите версию клиента, дистрибутив и режим подключения. Перед отправкой лога скройте ссылки подписок, адреса частных узлов и учётные данные.
