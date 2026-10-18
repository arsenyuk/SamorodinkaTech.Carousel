#!/usr/bin/env bash
# Build and run Mini App (Vite) on port 5173, API expected on 5050.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT/src/frontends/mini-app"

if [[ ! -d node_modules ]]; then
  echo "==> npm install"
  npm install
fi

echo "==> vite dev on http://localhost:5173 (API http://localhost:5050)"
exec env VITE_API_BASE_URL="${VITE_API_BASE_URL:-http://localhost:5050}" npm run dev -- --host 127.0.0.1 --port 5173
