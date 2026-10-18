#!/usr/bin/env bash
# Build and run Carousel API (Development + Telegram mock) on port 5050.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${PORT:-5050}"
ENV="${ASPNETCORE_ENVIRONMENT:-Development}"

cd "$ROOT"

mkdir -p logs

echo "==> build API"
dotnet build src/Carousel.Api/Carousel.Api.csproj -v q

echo "==> start API on http://127.0.0.1:${PORT} (env=${ENV}, Telegram mock on)"
echo "    health:  http://127.0.0.1:${PORT}/health"
echo "    swagger: http://127.0.0.1:${PORT}/swagger/index.html"
echo "    Ctrl+C to stop"
echo

exec env \
  ASPNETCORE_ENVIRONMENT="$ENV" \
  ASPNETCORE_URLS="http://127.0.0.1:${PORT}" \
  Telegram__UseMock="${Telegram__UseMock:-true}" \
  Telegram__MiniAppUrl="${Telegram__MiniAppUrl:-http://localhost:5173}" \
  Cors__AllowedOrigins__0="http://localhost:5173" \
  dotnet run --project src/Carousel.Api --no-build --no-launch-profile
