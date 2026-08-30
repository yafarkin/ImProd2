#!/usr/bin/env bash
#
# build-raspberrypi.sh — сборка «Производственных цепочек» (Game.Web) из исходников
# прямо на Raspberry Pi (Raspberry Pi OS / Debian). Ничего кросс-компилировать на
# другой машине не нужно: положил репозиторий на малинку, запустил этот скрипт —
# на выходе готовое приложение, которое стартует одним файлом.
#
# Что делает скрипт:
#   1. Определяет архитектуру Pi (arm64 / arm / x64) и подбирает .NET RID.
#   2. Проверяет наличие .NET SDK 8. Если его нет — скачивает и ставит локально
#      в каталог репозитория (./.dotnet); система при этом не трогается.
#   3. Публикует src/Game.Web как self-contained приложение: нужный .NET runtime
#      вшит внутрь, отдельно ничего ставить не надо.
#   4. Кладёт готовое приложение в отдельную папку (по умолчанию ~/improd) и создаёт:
#        - start.sh                      — запускалка из терминала;
#        - «Производственные цепочки».desktop — ярлык для двойного клика в файловом
#          менеджере и пункт в меню приложений.
#   5. По флагу --service ставит systemd-сервис, чтобы приложение поднималось само
#      при загрузке Pi (удобно для «безголовой» установки в зале).
#
# Порт и адрес прослушивания вшиты в исходники (src/Game.Web/Program.cs →
# builder.WebHost.UseUrls). По умолчанию это 0.0.0.0:5180 — сервер виден всем в
# локальной сети, как и задумано для мероприятия. Если нужен другой порт/адрес,
# скрипт на время сборки патчит эту одну строку и после сборки откатывает её.
#
# Использование:
#   ./build-raspberrypi.sh                  # публикация в ~/improd
#   ./build-raspberrypi.sh -o /путь/куда    # своя папка назначения
#   ./build-raspberrypi.sh --install-deps   # доставить системные пакеты (apt, sudo)
#   ./build-raspberrypi.sh --service        # + systemd-сервис (автозапуск при загрузке)
#   PORT=5200 ./build-raspberrypi.sh        # порт веб-сервера (по умолчанию 5180)
#   HOST=127.0.0.1 ./build-raspberrypi.sh   # адрес привязки (по умолчанию 0.0.0.0 — виден в сети)
#
set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Параметры
# ─────────────────────────────────────────────────────────────────────────────
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$REPO_DIR/src/Game.Web/Game.Web.csproj"
PROGRAM_CS="$REPO_DIR/src/Game.Web/Program.cs"
APP_NAME="ImProd"                       # имя .desktop-файла
APP_TITLE="Производственные цепочки"    # человекочитаемое имя в меню приложений
BIN_NAME="Game.Web"                     # имя опубликованного бинарника
DOTNET_CHANNEL="8.0"
PORT="${PORT:-5180}"
HOST="${HOST:-0.0.0.0}"
OUTPUT_DIR="${IMPROD_DIR:-${HOME}/improd}"
INSTALL_DEPS=0
INSTALL_SERVICE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output)    OUTPUT_DIR="$2"; shift 2 ;;
    --host)         HOST="$2"; shift 2 ;;
    --port)         PORT="$2"; shift 2 ;;
    --install-deps) INSTALL_DEPS=1; shift ;;
    --service)      INSTALL_SERVICE=1; shift ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 1 ;;
  esac
done

info()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn()  { printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
die()   { printf '\033[1;31m[x]\033[0m %s\n' "$*" >&2; exit 1; }

[[ "$(uname -s)" == "Linux" ]] || die "Скрипт рассчитан на Linux (Raspberry Pi OS / Debian)."
[[ -f "$PROJECT" ]] || die "Не найден проект: $PROJECT (запусти скрипт из корня репозитория)."
[[ -f "$PROGRAM_CS" ]] || die "Не найден $PROGRAM_CS."

download() { # url dest
  if command -v curl >/dev/null 2>&1; then curl -fsSL "$1" -o "$2"
  elif command -v wget >/dev/null 2>&1; then wget -qO "$2" "$1"
  else die "Нужен curl или wget."; fi
}

# ─────────────────────────────────────────────────────────────────────────────
# Патч порта/адреса в Program.cs (только если просят не дефолт) + гарантированный откат
# ─────────────────────────────────────────────────────────────────────────────
PATCHED=0
restore_program_cs() {
  if [[ "$PATCHED" -eq 1 && -f "$PROGRAM_CS.orig" ]]; then
    mv -f "$PROGRAM_CS.orig" "$PROGRAM_CS"
    info "Program.cs восстановлен в исходный вид."
  fi
}
trap restore_program_cs EXIT

DESIRED_URL="http://${HOST}:${PORT}"
if [[ "$DESIRED_URL" != "http://0.0.0.0:5180" ]]; then
  info "На время сборки правлю адрес в Program.cs → $DESIRED_URL (после сборки откатится)"
  cp "$PROGRAM_CS" "$PROGRAM_CS.orig"
  PATCHED=1
  sed -i -E "s#UseUrls\(\"http://[^\"]*\"\)#UseUrls(\"${DESIRED_URL}\")#" "$PROGRAM_CS"
  grep -q "UseUrls(\"${DESIRED_URL}\")" "$PROGRAM_CS" \
    || die "Не удалось пропатчить Program.cs — проверь строку builder.WebHost.UseUrls(...)."
fi

# ─────────────────────────────────────────────────────────────────────────────
# Определяем архитектуру → RID
# ─────────────────────────────────────────────────────────────────────────────
case "$(uname -m)" in
  aarch64|arm64)   RID="linux-arm64"; DOTNET_ARCH="arm64" ;;
  armv7l|armv8l)   RID="linux-arm";   DOTNET_ARCH="arm" ;;
  armv6l)          die "ARMv6 (Pi 1 / Pi Zero W) не поддерживается .NET. Нужен Pi 2+ / Zero 2." ;;
  x86_64|amd64)    RID="linux-x64";   DOTNET_ARCH="x64" ;;
  *) die "Неизвестная архитектура: $(uname -m)" ;;
esac
info "Архитектура: $(uname -m) → $RID"

# ─────────────────────────────────────────────────────────────────────────────
# Системные пакеты (по флагу --install-deps)
# ─────────────────────────────────────────────────────────────────────────────
# Приложение self-contained, а в Directory.Build.props InvariantGlobalization=true,
# поэтому ICU не нужен. Достаточно базовых библиотек, которые на Raspberry Pi OS
# Bookworm уже стоят.
DEPS="curl ca-certificates libstdc++6 zlib1g libssl3 libgcc-s1"
if [[ "$INSTALL_DEPS" -eq 1 ]]; then
  info "Ставлю системные пакеты: $DEPS"
  sudo apt-get update
  for pkg in $DEPS; do
    sudo apt-get install -y "$pkg" || warn "Пакет $pkg не установлен (возможно, он не нужен в этой версии ОС)."
  done
fi

# ─────────────────────────────────────────────────────────────────────────────
# Ищем .NET SDK 8. Если нет — ставим локально в ./.dotnet
# ─────────────────────────────────────────────────────────────────────────────
LOCAL_DOTNET_DIR="$REPO_DIR/.dotnet"

have_sdk() {
  local dotnet_bin="$1"
  command -v "$dotnet_bin" >/dev/null 2>&1 || return 1
  "$dotnet_bin" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL//./\\.}\." || return 1
}

DOTNET=""
if have_sdk dotnet; then
  DOTNET="$(command -v dotnet)"
  info "Найден системный .NET SDK: $("$DOTNET" --version)"
elif have_sdk "$LOCAL_DOTNET_DIR/dotnet"; then
  DOTNET="$LOCAL_DOTNET_DIR/dotnet"
  info "Найден локальный .NET SDK: $("$DOTNET" --version)"
else
  warn ".NET SDK $DOTNET_CHANNEL не найден — ставлю локально в $LOCAL_DOTNET_DIR"
  INSTALL_SCRIPT="$(mktemp)"
  download https://dot.net/v1/dotnet-install.sh "$INSTALL_SCRIPT"
  chmod +x "$INSTALL_SCRIPT"
  "$INSTALL_SCRIPT" --channel "$DOTNET_CHANNEL" --architecture "$DOTNET_ARCH" --install-dir "$LOCAL_DOTNET_DIR"
  rm -f "$INSTALL_SCRIPT"
  DOTNET="$LOCAL_DOTNET_DIR/dotnet"
  have_sdk "$DOTNET" || die "Установка .NET SDK не удалась."
  info "Установлен .NET SDK: $("$DOTNET" --version)"
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# ─────────────────────────────────────────────────────────────────────────────
# Публикация (self-contained)
# ─────────────────────────────────────────────────────────────────────────────
info "Очищаю папку назначения: $OUTPUT_DIR"
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

info "Собираю и публикую (на Pi это может занять несколько минут)…"
# self-contained: нужный .NET runtime кладётся рядом с приложением, ставить ничего
# не нужно. Single-file не используем — запускалка и так прячет остальные файлы.
"$DOTNET" publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$OUTPUT_DIR"

APP_BIN="$OUTPUT_DIR/$BIN_NAME"
[[ -f "$APP_BIN" ]] || die "После публикации не найден бинарник: $APP_BIN"
chmod +x "$APP_BIN"

# Порт/адрес откатываем в исходниках сразу — дальше он уже вшит в бинарник.
restore_program_cs
PATCHED=0

# ─────────────────────────────────────────────────────────────────────────────
# Запускалка для терминала
# ─────────────────────────────────────────────────────────────────────────────
LAUNCHER="$OUTPUT_DIR/start.sh"
cat > "$LAUNCHER" <<LAUNCHER_EOF
#!/usr/bin/env bash
# Запуск «Производственных цепочек» (Game.Web). Останов: Ctrl+C.
# Из корня репозитория это же делает ./run.sh (он ещё проверяет, что сборка на месте).
set -e
cd "\$(dirname "\$0")"

export ASPNETCORE_ENVIRONMENT="\${ASPNETCORE_ENVIRONMENT:-Development}"
# Порт/адрес вшиты в сборку (Program.cs → UseUrls). Значение ниже — только справка
# и подстраховка на случай, если в исходниках уберут хардкод.
export ASPNETCORE_URLS="\${ASPNETCORE_URLS:-http://${HOST}:${PORT}}"

PORT="${PORT}"
LOCAL_URL="http://localhost:\${PORT}"
LAN_IP="\$(hostname -I 2>/dev/null | awk '{print \$1}')"

echo "«Производственные цепочки» запускаются:"
echo "  локально:   \$LOCAL_URL"
[ -n "\$LAN_IP" ] && echo "  в сети:     http://\${LAN_IP}:\${PORT}   (и http://\$(hostname).local:\${PORT})"
echo "  Код администратора для настройки сессии печатается ниже при старте."

# Если есть графическая сессия — откроем браузер, когда сервер поднимется
if [ -n "\${DISPLAY:-}\${WAYLAND_DISPLAY:-}" ] && command -v xdg-open >/dev/null 2>&1; then
  (
    for _ in \$(seq 1 60); do
      if curl -s -o /dev/null "\$LOCAL_URL"; then break; fi
      sleep 0.5
    done
    xdg-open "\$LOCAL_URL" >/dev/null 2>&1 || true
  ) &
fi

exec ./$BIN_NAME
LAUNCHER_EOF
chmod +x "$LAUNCHER"

# ─────────────────────────────────────────────────────────────────────────────
# Ярлык .desktop — двойной клик в файловом менеджере и пункт в меню приложений
# ─────────────────────────────────────────────────────────────────────────────
DESKTOP_FILE="$OUTPUT_DIR/${APP_NAME}.desktop"
cat > "$DESKTOP_FILE" <<DESKTOP_EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=$APP_TITLE
Comment=Запустить сервер «Производственные цепочки»
Exec=$LAUNCHER
Path=$OUTPUT_DIR
Icon=$OUTPUT_DIR/wwwroot/favicon.png
Terminal=true
Categories=Education;Development;
DESKTOP_EOF
chmod +x "$DESKTOP_FILE"

# Регистрируем в меню приложений
APPS_DIR="$HOME/.local/share/applications"
mkdir -p "$APPS_DIR"
cp "$DESKTOP_FILE" "$APPS_DIR/improd.desktop"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$APPS_DIR" >/dev/null 2>&1 || true

# Кладём ярлык на рабочий стол (если он есть) и помечаем доверенным
DESKTOP_DIR="$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")"
if [[ -d "$DESKTOP_DIR" ]]; then
  cp "$DESKTOP_FILE" "$DESKTOP_DIR/improd.desktop"
  chmod +x "$DESKTOP_DIR/improd.desktop"
  gio set "$DESKTOP_DIR/improd.desktop" metadata::trusted true 2>/dev/null || true
fi

# ─────────────────────────────────────────────────────────────────────────────
# systemd-сервис (по флагу --service) — автозапуск при загрузке Pi
# ─────────────────────────────────────────────────────────────────────────────
if [[ "$INSTALL_SERVICE" -eq 1 ]]; then
  UNIT_DIR="$HOME/.config/systemd/user"
  mkdir -p "$UNIT_DIR"
  cat > "$UNIT_DIR/improd.service" <<UNIT_EOF
[Unit]
Description=Производственные цепочки (Game.Web)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$OUTPUT_DIR
ExecStart=$OUTPUT_DIR/$BIN_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://$HOST:$PORT
Restart=on-failure
RestartSec=3

[Install]
WantedBy=default.target
UNIT_EOF

  systemctl --user daemon-reload
  systemctl --user enable --now improd.service
  # Чтобы сервис работал без входа пользователя в систему (headless)
  sudo loginctl enable-linger "$USER" 2>/dev/null || \
    warn "Не удалось включить linger — сервис стартует только после входа в систему. Выполни: sudo loginctl enable-linger $USER"
  info "systemd-сервис установлен и запущен (Production)."
fi

# ─────────────────────────────────────────────────────────────────────────────
LAN_IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
info "Готово!"
echo
echo "  Приложение:  $OUTPUT_DIR"
echo
echo "  Управление (из корня репозитория, по SSH):"
echo "     ./run.sh            # запустить в фоне — переживает выход из SSH"
echo "     ./run.sh logs       # смотреть лог (коды входа администратора — там, при старте)"
echo "     ./run.sh status     # работает ли, PID, адрес"
echo "     ./run.sh stop       # корректно остановить"
echo
echo "  Ещё варианты запуска: двойной клик по «${APP_TITLE}» в меню приложений;"
echo "  на переднем плане — \"$LAUNCHER\"  (или  ./run.sh run)."
echo
echo "  Веб-интерфейс:"
echo "               локально:  http://localhost:${PORT}"
[[ -n "$LAN_IP" && "$HOST" != "127.0.0.1" ]] && \
echo "               в сети:    http://${LAN_IP}:${PORT}   (и http://$(hostname).local:${PORT})"
if [[ "$INSTALL_SERVICE" -eq 1 ]]; then
  echo
  echo "  Сервис:      systemctl --user status improd     # состояние"
  echo "               systemctl --user restart improd    # перезапуск"
  echo "               journalctl --user -u improd -f     # логи (там же код администратора)"
fi
