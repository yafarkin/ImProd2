#!/usr/bin/env bash
#
# run.sh — запустить уже собранные «Производственные цепочки» (Game.Web).
#
# Скрипт проверяет, что приложение собрано и готово к запуску. Если сборки нет —
# он не пытается ничего собирать, а подсказывает запустить:
#
#     ./build-raspberrypi.sh
#
# После сборки повторный вызов ./run.sh поднимает сервер (Ctrl+C — остановить).
#
# Использование:
#   ./run.sh                       # запуск из папки по умолчанию (~/Applications/ImProd)
#   ./run.sh -o /путь/к/сборке     # если публиковали в свою папку (build-raspberrypi.sh -o …)
#   IMPROD_DIR=/путь ./run.sh      # то же через переменную окружения
#
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_NAME="ImProd"
BIN_NAME="Game.Web"
BUILD_SCRIPT="./build-raspberrypi.sh"
OUTPUT_DIR="${IMPROD_DIR:-$HOME/Applications/$APP_NAME}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--output) OUTPUT_DIR="$2"; shift 2 ;;
    -h|--help)   grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Неизвестный аргумент: $1" >&2; exit 1 ;;
  esac
done

info() { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[!]\033[0m %s\n' "$*"; }

APP_BIN="$OUTPUT_DIR/$BIN_NAME"
LAUNCHER="$OUTPUT_DIR/start.sh"

# ── Готово ли к запуску? ─────────────────────────────────────────────────────
if [[ ! -x "$APP_BIN" || ! -f "$LAUNCHER" ]]; then
  printf '\033[1;31m[x]\033[0m %s\n' "Приложение не собрано — не найдено: $APP_BIN" >&2
  cat >&2 <<MSG

    Собери его из исходников на этой машине:

        $BUILD_SCRIPT

    …и запусти снова:

        ./run.sh

    Если публиковали в нестандартную папку — укажи её:  ./run.sh -o /путь/к/сборке
MSG
  exit 1
fi

# ── Не устарела ли сборка? (не блокирует, просто подсказка) ──────────────────
if [[ -d "$REPO_DIR/src" ]]; then
  NEWER="$(find "$REPO_DIR/src" -type f -name '*.cs' -newer "$APP_BIN" \
            -not -path '*/bin/*' -not -path '*/obj/*' -print -quit 2>/dev/null || true)"
  if [[ -n "$NEWER" ]]; then
    warn "Исходники менялись после последней сборки — возможно, стоит пересобрать: $BUILD_SCRIPT"
  fi
fi

# ── Запуск ──────────────────────────────────────────────────────────────────
info "Запускаю из $OUTPUT_DIR"
exec "$LAUNCHER"
