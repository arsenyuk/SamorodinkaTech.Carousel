#!/usr/bin/env bash
# Wait until API + Mini App are up, then open the Mini App in the browser.
# Usage: ./scripts/open-miniapp.sh [--wait] [--tg-id <telegramUserId>]
set -euo pipefail

API_URL="${API_URL:-http://127.0.0.1:5050}"
MINI_URL_BASE="${MINI_URL_BASE:-http://localhost:5173}"
TG_ID="${TG_ID:-123456789}"
WAIT=0
TIMEOUT_SEC="${TIMEOUT_SEC:-30}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --wait) WAIT=1; shift ;;
    --tg-id) TG_ID="${2:?--tg-id requires value}"; shift 2 ;;
    -h|--help)
      echo "Usage: $0 [--wait] [--tg-id <id>]"
      echo "  --wait     poll until API and Mini App respond (default: fail if down)"
      echo "  --tg-id    Telegram user id for Mini App dev login query (default: 123456789)"
      exit 0
      ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

api_up() { curl -sf --max-time 2 "${API_URL}/health" >/dev/null 2>&1; }
mini_up() { curl -sf --max-time 2 "${MINI_URL_BASE}/" >/dev/null 2>&1; }

wait_for() {
  local name="$1" fn="$2" deadline=$((SECONDS + TIMEOUT_SEC))
  while ! "$fn"; do
    if (( SECONDS >= deadline )); then
      echo "ERROR: ${name} is not responding after ${TIMEOUT_SEC}s (${API_URL} / ${MINI_URL_BASE})" >&2
      echo "Start it first, e.g.:" >&2
      echo "  VS Code: Run and Debug → Carousel API (Development + Telegram mock) + Mini App (Vite)" >&2
      echo "  or:      ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=${API_URL} dotnet run --project src/Carousel.Api" >&2
      echo "  and:     cd src/frontends/mini-app && npm run dev" >&2
      exit 1
    fi
    printf '.'
    sleep 0.5
  done
  echo " ${name}: OK"
}

if [[ "$WAIT" -eq 1 ]]; then
  echo "Waiting for API (${API_URL})…"
  wait_for "API" api_up
  echo "Waiting for Mini App (${MINI_URL_BASE})…"
  wait_for "Mini App" mini_up
else
  if ! api_up; then
    echo "ERROR: API is not running at ${API_URL}/health" >&2
    echo "Start API first (VS Code launch or dotnet run), then re-run this script." >&2
    echo "Or use: $0 --wait" >&2
    exit 1
  fi
  echo "API: OK"

  if ! mini_up; then
    echo "ERROR: Mini App is not running at ${MINI_URL_BASE}" >&2
    echo "Start Vite first (VS Code launch 'Mini App (Vite)' or npm run dev)." >&2
    echo "Or use: $0 --wait" >&2
    exit 1
  fi
  echo "Mini App: OK"
fi

URL="${MINI_URL_BASE}/?tgInitData=${TG_ID}"
echo "Opening ${URL}"
open "${URL}"
