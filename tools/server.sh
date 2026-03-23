#!/usr/bin/env bash
# server.sh — Cycle the tournament server (kill → restart → verify)
# Usage:
#   tools/server.sh restart        Kill existing, start fresh
#   tools/server.sh restart-clean  Kill existing, clear generations, start fresh
#   tools/server.sh stop           Kill existing
#   tools/server.sh status         Check if server is running

set -euo pipefail

GODOT="/c/Program Files/godot/godot_console.exe"
PORT="${PORT:-8585}"
URL="http://localhost:${PORT}"
SCENE="res://scenes/tournament_runner.tscn"

stop_server() {
    echo "Killing godot_console.exe..."
    taskkill //F //IM godot_console.exe 2>/dev/null && echo "Killed." || echo "No process found."
    echo "Waiting for port release..."
    sleep 3
}

start_server() {
    echo "Starting tournament server on port ${PORT}..."
    "$GODOT" --headless --scene "$SCENE" &
    # Wait for server to become responsive
    for i in $(seq 1 15); do
        sleep 1
        if curl -s "${URL}/api/status" >/dev/null 2>&1; then
            echo "Server is up: ${URL}"
            curl -s "${URL}/api/status"
            echo ""
            return 0
        fi
        echo "  waiting... (${i}s)"
    done
    echo "ERROR: Server failed to start within 15s"
    return 1
}

clean_generations() {
    # Archive existing generations into a timestamped folder
    if ls generations/gen_* 1>/dev/null 2>&1; then
        archive="generations/archive_$(date +%Y%m%d_%H%M%S)"
        mkdir -p "$archive"
        mv generations/gen_* "$archive/"
        echo "Archived to $archive"
    else
        echo "No generation data to archive."
    fi
}

case "${1:-}" in
    restart)
        stop_server
        start_server
        ;;
    restart-clean)
        stop_server
        clean_generations
        start_server
        ;;
    stop)
        stop_server
        ;;
    status)
        curl -s "${URL}/api/status" 2>/dev/null || echo "Server is not running."
        ;;
    *)
        echo "Usage: tools/server.sh {restart|restart-clean|stop|status}"
        exit 1
        ;;
esac
