#!/usr/bin/env bash
#
# run.sh — управление сервером «Производственные цепочки» (Game.Web) на Raspberry Pi.
#
# Рассчитано на работу по SSH: запустил в фоне, вышел из сессии — сервер продолжает
# работать; зашёл позже — посмотрел лог (там же коды входа администратора) и корректно
# остановил.
#
#   ./run.sh            запустить в фоне (то же, что ./run.sh start)
#   ./run.sh start      то же самое
#   ./run.sh stop       корректно остановить (SIGTERM → добить через 20 с)
#   ./run.sh restart    перезапустить
#   ./run.sh status     работает ли, PID, адрес, путь к логу
#   ./run.sh logs       показать лог и следить дальше (Ctrl+C — выйти, сервер не трогает)
#   ./run.sh run        запустить на переднем плане (Ctrl+C останавливает сервер)
#
# Если приложение ещё не собрано — скрипт ничего не собирает сам, а просит запустить
# ./build-raspberrypi.sh.
#
# Каталог сборки по умолчанию ~/improd; переопределяется:  ./run.sh -o /путь  или
# IMPROD_DIR=/путь ./run.sh . Логи — в <каталог>/logs/, символьная ссылка latest.log
# указывает на текущий запуск.
#
set -euo pipefail

APP_NAME="ImProd"
BIN_NAME="Game.Web"
BUILD_SCRIPT="./build-raspberrypi.sh"
STOP_WAIT_SECONDS=20

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="${IMPROD_DIR:-$HOME/improd}"

CMD=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output)                 OUTPUT_DIR="$2"; shift 2 ;;
    start|stop|restart|status|logs|run)
                                 CMD="$1"; shift ;;
    -h|--help)                   grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 1 ;;
  esac
done
CMD="${CMD:-start}"

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[!]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[x]\033[0m %s\n' "$*" >&2; }

APP_BIN="$OUTPUT_DIR/$BIN_NAME"
LAUNCHER="$OUTPUT_DIR/start.sh"
PID_FILE="$OUTPUT_DIR/improd.pid"
LOG_DIR="$OUTPUT_DIR/logs"
LATEST_LOG="$LOG_DIR/latest.log"

require_build() {
  if [[ ! -x "$APP_BIN" || ! -f "$LAUNCHER" ]]; then
    err "Приложение не собрано — не найдено: $APP_BIN"
    cat >&2 <<MSG

    Собери его из исходников на этой машине:

        $BUILD_SCRIPT

    …и запусти снова:

        ./run.sh

    Если публиковали в нестандартную папку — укажи её:  ./run.sh -o /путь/к/сборке
MSG
    exit 1
  fi
}

# Порт вшит в сборку — достаём его из start.sh, чтобы верно показать адрес.
detect_port() {
  local p=""
  if [[ -f "$LAUNCHER" ]]; then
    p="$(grep -oE 'http://[^:"]*:[0-9]+' "$LAUNCHER" 2>/dev/null | grep -oE '[0-9]+$' | head -n1 || true)"
  fi
  echo "${p:-5180}"
}

show_urls() {
  local port ip host
  port="$(detect_port)"
  ip="$(hostname -I 2>/dev/null | awk '{print $1}' || true)"
  host="$(hostname 2>/dev/null || true)"
  echo "  адрес:    http://localhost:${port}"
  if [[ -n "$ip" ]]; then
    echo "            http://${ip}:${port}${host:+   (http://${host}.local:${port})}"
  fi
}

# RUNNING_PID выставляется, если сервер жив.
RUNNING_PID=""
is_running() {
  local pid
  [[ -f "$PID_FILE" ]] || return 1
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  [[ -n "$pid" ]] || return 1
  kill -0 "$pid" 2>/dev/null || return 1
  # Защита от повторно выданного системой PID: если /proc/<pid>/exe читается и указывает
  # на ЧУЖОЙ бинарник — считаем, что наш процесс уже мёртв. Если прочитать нельзя
  # (нет /proc, нет прав) — доверяем PID-файлу и kill -0.
  local exe want
  exe="$(readlink -f "/proc/$pid/exe" 2>/dev/null || true)"
  want="$(readlink -f "$APP_BIN" 2>/dev/null || echo "$APP_BIN")"
  if [[ -n "$exe" && "$exe" != "$want" ]]; then
    return 1
  fi
  RUNNING_PID="$pid"
  return 0
}

warn_if_stale() {
  [[ -d "$REPO_DIR/src" ]] || return 0
  local newer
  newer="$(find "$REPO_DIR/src" -type f -name '*.cs' -newer "$APP_BIN" \
             -not -path '*/bin/*' -not -path '*/obj/*' -print -quit 2>/dev/null || true)"
  [[ -n "$newer" ]] && warn "Исходники менялись после последней сборки — возможно, стоит пересобрать: $BUILD_SCRIPT"
  return 0
}

cmd_start() {
  require_build
  if is_running; then
    warn "Уже запущено (PID $RUNNING_PID)."
    show_urls
    echo "  лог:      ./run.sh logs      стоп:  ./run.sh stop"
    return 0
  fi
  # подчистим устаревший PID-файл от прошлого запуска
  [[ -f "$PID_FILE" ]] && rm -f "$PID_FILE"
  warn_if_stale

  mkdir -p "$LOG_DIR"
  local ts log
  ts="$(date +%Y%m%d-%H%M%S)"
  log="$LOG_DIR/improd-$ts.log"
  : > "$log"
  ln -sfn "$(basename "$log")" "$LATEST_LOG"

  # Полностью отвязываем от SSH-сессии: новый сеанс (setsid) + stdin из /dev/null.
  # setsid без -w может форкнуться, поэтому не полагаемся на $! — сам процесс
  # (bash -c → exec start.sh → exec Game.Web, PID сквозной) пишет свой PID в файл.
  local spawn_cmd='echo $$ > "$2"; exec bash "$1"'
  if command -v setsid >/dev/null 2>&1; then
    setsid bash -c "$spawn_cmd" _ "$LAUNCHER" "$PID_FILE" >>"$log" 2>&1 </dev/null &
  else
    nohup bash -c "$spawn_cmd" _ "$LAUNCHER" "$PID_FILE" >>"$log" 2>&1 </dev/null &
  fi
  disown 2>/dev/null || true

  local pid="" i
  for ((i = 0; i < 25; i++)); do
    if [[ -s "$PID_FILE" ]]; then pid="$(cat "$PID_FILE" 2>/dev/null || true)"; [[ -n "$pid" ]] && break; fi
    sleep 0.2
  done
  if [[ -z "$pid" ]]; then
    err "Не удалось определить PID запущенного процесса. Последние строки лога ($log):"
    tail -n 25 "$log" >&2 || true
    exit 1
  fi

  # убедимся, что не упал сразу
  sleep 2
  if ! kill -0 "$pid" 2>/dev/null; then
    err "Сервер не поднялся. Последние строки лога ($log):"
    tail -n 25 "$log" >&2 || true
    rm -f "$PID_FILE"
    exit 1
  fi

  info "Запущен в фоне, PID $pid (переживёт выход из SSH)."
  show_urls
  echo "  лог:      ./run.sh logs      (коды входа администратора — там, при старте)"
  echo "  стоп:     ./run.sh stop"
}

cmd_stop() {
  if ! is_running; then
    warn "Сервер не запущен."
    [[ -f "$PID_FILE" ]] && rm -f "$PID_FILE"
    return 0
  fi
  local pid="$RUNNING_PID"
  info "Останавливаю PID $pid (SIGTERM, graceful)…"
  kill -TERM "$pid" 2>/dev/null || true
  local i
  for ((i = 0; i < STOP_WAIT_SECONDS * 2; i++)); do
    kill -0 "$pid" 2>/dev/null || break
    sleep 0.5
  done
  if kill -0 "$pid" 2>/dev/null; then
    warn "Не завершился за ${STOP_WAIT_SECONDS} с — добиваю (SIGKILL)."
    kill -KILL "$pid" 2>/dev/null || true
    sleep 1
  fi
  rm -f "$PID_FILE"
  info "Остановлен."
}

cmd_status() {
  if is_running; then
    info "Работает. PID $RUNNING_PID"
    show_urls
    [[ -e "$LATEST_LOG" ]] && echo "  лог:      $(readlink -f "$LATEST_LOG" 2>/dev/null || echo "$LATEST_LOG")"
    return 0
  fi
  warn "Сервер не запущен."
  [[ -e "$LATEST_LOG" ]] && echo "  прошлый лог: $(readlink -f "$LATEST_LOG" 2>/dev/null || echo "$LATEST_LOG")"
  exit 1
}

cmd_logs() {
  if [[ ! -e "$LATEST_LOG" ]]; then
    err "Логов ещё нет — сервер не запускался в фоне (см. ./run.sh start)."
    exit 1
  fi
  info "Лог: $(readlink -f "$LATEST_LOG" 2>/dev/null || echo "$LATEST_LOG")"
  info "Ctrl+C — выйти из просмотра (сервер продолжит работать)."
  echo
  exec tail -n 200 -F "$LATEST_LOG"
}

cmd_run() {
  require_build
  if is_running; then
    err "Сервер уже запущен в фоне (PID $RUNNING_PID). Останови его: ./run.sh stop"
    exit 1
  fi
  warn_if_stale
  info "Передний план. Ctrl+C — остановить сервер."
  echo
  exec "$LAUNCHER"
}

case "$CMD" in
  start)   cmd_start ;;
  stop)    cmd_stop ;;
  restart) cmd_stop; echo; cmd_start ;;
  status)  cmd_status ;;
  logs)    cmd_logs ;;
  run)     cmd_run ;;
esac
