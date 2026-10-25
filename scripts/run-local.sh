#!/usr/bin/env bash
# Starts the whole local stack: Azurite -> Functions API -> Blazor client -> SWA proxy.
# Open http://localhost:4280 (NOT :5000 — the cookie has to be same-origin).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="$ROOT/.local-run"
mkdir -p "$LOGS/azurite-data"

cleanup() {
  echo
  echo "Shutting down..."
  for pid in "${PIDS[@]:-}"; do kill "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT INT TERM
PIDS=()

wait_for_port() {
  local port=$1 name=$2 tries=60
  while ! (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; do
    ((tries--)) || { echo "$name never came up on :$port"; exit 1; }
    sleep 1
  done
  exec 3>&- 2>/dev/null || true
  echo "  $name ready on :$port"
}

echo "1/4 Azurite (storage emulator)"
azurite --location "$LOGS/azurite-data" --silent \
        --blobPort 10000 --queuePort 10001 --tablePort 10002 > "$LOGS/azurite.log" 2>&1 &
PIDS+=($!)
wait_for_port 10002 "Azurite"

# func's project detection globs recursively and trips over the SDK-generated
# obj/**/WorkerExtensions.csproj, so we build first and point it at the output.
echo "2/4 Building API"
dotnet build "$ROOT/src/Api" -v q --nologo > "$LOGS/build.log" 2>&1
echo "  built"

echo "3/4 Functions host"
func start --script-root "$ROOT/src/Api/bin/output" --port 7071 > "$LOGS/func.log" 2>&1 &
PIDS+=($!)
wait_for_port 7071 "API"

echo "4/4 Blazor client + SWA proxy"
dotnet run --project "$ROOT/src/Client" --urls http://localhost:5000 > "$LOGS/client.log" 2>&1 &
PIDS+=($!)
wait_for_port 5000 "Client"

swa start http://localhost:5000 --api-devserver-url http://localhost:7071 --port 4280 \
  > "$LOGS/swa.log" 2>&1 &
PIDS+=($!)
wait_for_port 4280 "Proxy"

echo
echo "  Open http://localhost:4280   (password: badlands)"
echo "  Logs in $LOGS/ — Ctrl+C to stop everything."
wait
