# SplateVPN для Linux

Порт исходников SplateClientDesktop 2.14.0. Интерфейс, модели, конфигурации Xray,
подписки, профили, маршруты, SmartDNS, фильтры и алгоритм балансировки перенесены
из Desktop. Вместо WPF/WebView2 используется Photino с WebKitGTK.

## Установка

Целевая платформа: **Linux x86_64**, Ubuntu 24.04+, Linux Mint 22 Cinnamon,
Debian 13+ с графическим рабочим столом. На данный момент фактические проверки
выполнены в Ubuntu 26.04 под WSL2; отдельный запуск на Mint и Debian ещё нужен.
Дистрибутивы с WebKitGTK 4.0 без 4.1 этой сборкой не поддерживаются.
Нативная оболочка требует glibc 2.38+ и libstdc++ с GLIBCXX_3.4.31;
готовый пакет поэтому не подходит для Debian 12 и Mint 21.

Готовые пакеты: [GitHub Releases](https://github.com/splatedk-web/splatevpn-xray-client-linux/releases/latest).
Скачанный пакет устанавливается так:

```sh
sudo apt install ./splatevpn_2.14.1-1_amd64.deb
```

Запуск из меню приложений или `/opt/splatevpn/SplateClient`.
.NET включён в пакет. GTK/WebKitGTK и прочие системные зависимости ставит apt.
Приложение запускается обычным пользователем. Только сетевой помощник TUN
запрашивает административные права через polkit.

## Обязательные возможности

- **Балансировщик:** исходные режимы `balanced`, `failover`, `initial_only`,
  подтверждение отказа, резерв, пауза переключений и остановка оптимизации во
  время спидтеста/загрузки. Для новых установок включён по умолчанию.
- **Входящие подключения:** TUN не меняет политику firewall и основной default
  route. Ответы с физических IPv4/IPv6-адресов направляются через основную таблицу,
  а новый исходящий трафик — в отдельную VPN-таблицу. Изменение адресов учитывается
  во время работы. Проверены входящие TCP при включённом строгом `rp_filter`.
  Это сохраняет доступность служб на адресах компьютера; проброс порта со стороны
  VPN-провайдера, NAT роутера и уже существующие запреты firewall настраиваются
  отдельно.
- **Автозапуск:** включён для новых установок, настраивается в интерфейсе.
  Файл `$XDG_CONFIG_HOME/autostart/splatevpn.desktop` (обычно `~/.config/autostart`)
  запускает клиент свёрнутым после входа в рабочий стол. Это запуск приложения,
  подключение VPN выполняется пользователем. Исходный Desktop также не сохраняет
  отдельную настройку автоматического подключения.

GNOME и Cinnamon используют системный прокси через `org.gnome.system.proxy`.
Значения сохраняются и восстанавливаются, если их не меняла другая программа.
Трей использует AppIndicator; GNOME Shell может потребоваться расширение
AppIndicator, обычно установленное в Ubuntu. Окно остаётся доступно через панель
задач и повторный запуск из меню.

Глобальная клавиша Super+Alt+V регистрируется в X11. Для Wayland добавьте в
настройках клавиатуры рабочего стола сочетание для команды:

```sh
/opt/splatevpn/SplateClient --toggle
```

Новые стабильные версии проверяются в этом Linux-репозитории при запуске
и кнопкой «Проверить обновления» в настройках. Если опубликована более новая
версия с готовым пакетом amd64, приложение показывает уведомление и кнопку
перехода на страницу релиза. Скачивание и установка остаются под управлением
пользователя.

## Данные и сеть

Настройки: `$XDG_CONFIG_HOME/SplateVPN` (обычно `~/.config/SplateVPN`).
Данные фильтров и геобаз: `$XDG_DATA_HOME/SplateVPN` (обычно `~/.local/share/SplateVPN`).
Импорт резервных копий Desktop доступен в интерфейсе. Исходный Windows-проект
при переносе не изменялся; хэши источников записаны в `docs/desktop-source-manifest.json`.

TUN владеет адаптером `SplateTUN`, таблицей маршрутов 20214 и приоритетами
22000–22003. При конфликте помощник прекращает запуск. При отключении, отмене,
закрытии stdin или смерти процесса GUI удаляются только созданные им ресурсы.
Xray запускается через наблюдающий процесс, чтобы не оставаться после смерти GUI.
Для привязки сокетов к физическому интерфейсу пакет выдаёт только `cap_net_raw`
двоичному файлу Xray; GUI не получает capabilities и не запускается от root.

Kill switch и отдельная глобальная защита DNS отсутствовали как рабочие
возможности в исходной Desktop-сборке и здесь также недоступны. Не выдавайте их
за проверенную защиту. Для программ без поддержки прокси используйте TUN.

## Сборка и проверка

```sh
dotnet publish -c Release -r linux-x64 --self-contained true -o dist/linux-x64
chmod +x dist/linux-x64/SplateClient dist/linux-x64/core/xray dist/linux-x64/core/xray.bin dist/linux-x64/core/tun2socks
sh scripts/build-deb.sh

dotnet publish tests/PortChecks -c Release -r linux-x64 --self-contained true -o dist/checks
dist/checks/PortChecks "$PWD"
sudo python3 tests/network_namespace_check.py
dbus-run-session -- xvfb-run -a python3 tests/ui_smoke.py
```

Сетевой тест создаёт две отдельные network namespace и не меняет сеть хоста.
Тест прокси запускайте с отдельными XDG-каталогами и `GSETTINGS_BACKEND=keyfile`:

```sh
dbus-run-session -- env XDG_CONFIG_HOME=/tmp/splate-proxy-test XDG_DATA_HOME=/tmp/splate-proxy-data GSETTINGS_BACKEND=keyfile dist/checks/PortChecks --proxy-only
```

Проверенные версии ядер и SHA-256: `docs/linux-core-manifest.json`.
Снимок настоящего Linux-окна и отчёт IPC: `tests/artifacts/linux-ui.png`,
`tests/artifacts/ui-smoke.json`. Подробности и границы проверок — `docs/VALIDATION.md`.
