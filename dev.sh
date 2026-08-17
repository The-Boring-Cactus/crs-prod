#!/usr/bin/env bash
# dev.sh — Run CRS Reporter for local development (Linux/macOS)
#
# Starts the .NET server (via `dotnet watch`, auto-rebuilds on save) and the
# Vite dev server for the Vue UI side by side. Ctrl+C stops both.
#
# Usage:
#   ./dev.sh
#
# Requirements:
#   - Node.js + npm  (for the Vue UI)
#   - .NET 10 SDK    (for the C# server)
#
# URLs:
#   UI (Vite, hot reload):  http://localhost:5173
#   Server (API/WebSocket): http://localhost:9876
#
# The UI dev server talks to the backend via ui/.env (VITE_API_URL /
# VITE_WS_URL), not a Vite proxy — no extra config needed as long as the
# server runs on its default port 9876. Override with CRS_PORT below if you
# change it (and update ui/.env to match).
#
# Optional env vars:
#   CRS_PORT=8080  — run the server on a different port (default: 9876)
#
# ─────────────────────────────────────────────────────────────────────────────

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo ""
echo "╔══════════════════════════════════════════╗"
echo "║   CRS Reporter — Dev Environment         ║"
echo "╚══════════════════════════════════════════╝"
echo ""

PIDS=()
cleanup() {
    echo ""
    echo "▶ Shutting down..."
    for pid in "${PIDS[@]}"; do
        kill "$pid" 2>/dev/null
    done
    wait 2>/dev/null
}
trap cleanup EXIT INT TERM

# ── 1. .NET server (auto-rebuild on change) ─────────────────────────────────
echo "▶ [1/2] Starting .NET server (dotnet watch)..."
(
    cd "$SCRIPT_DIR"
    CRS_ENV=development dotnet watch run --project Server/Server.csproj --non-interactive
) &
PIDS+=($!)

# ── 2. Vue UI (Vite dev server, hot reload) ─────────────────────────────────
echo "▶ [2/2] Starting Vite dev server..."
(
    cd "$SCRIPT_DIR/ui"
    if [ ! -d node_modules ]; then
        echo "  node_modules missing — running npm install first..."
        npm install
    fi
    npm run dev
) &
PIDS+=($!)

echo ""
echo "  Server:  http://localhost:${CRS_PORT:-9876}"
echo "  UI:      http://localhost:5173"
echo ""
echo "  Press Ctrl+C to stop both."
echo ""

wait
