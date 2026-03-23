# CLAUDE.md

## Project
AI-BT-Gym — Evolutionary behavior tree gym in Godot 4.6 (C#).

## Godot
- **Godot executable**: `"<path-to-godot-console>"` (headless-capable)
- **Editor**: `"<path-to-godot-editor>"`
- **Version**: 4.6.1 stable mono

## Build & Test
```bash
# Build
dotnet build

# Run tests headlessly
"<godot-console>" --headless --scene res://scenes/test_runner.tscn

# Run the game
"<godot-console>" --scene res://scenes/main.tscn
```

## Key Paths
- Simulation core: `simulation/`
- BT system: `simulation/BehaviorTree/`
- Godot scripts: `scripts/`
- Tests: `tests/MovementTests.cs` (run via `scenes/test_runner.tscn`)
- Test BTs: `scripts/TestTrees.cs`
- Seed AI definitions: `seed_generation/SeedTrees.cs`
- Tournament output: `generations/` (e.g. `generations/gen_000/`)
- Analysis scripts: `tools/`

## Tournament Server
- Scene: `scenes/tournament_runner.tscn`
- Script: `scripts/TournamentRunner.cs`
- URL: http://localhost:8585 (port configurable via `PORT` env var)
- The server runs inside Godot headless and stays alive to serve results

```bash
# Start tournament server
"<godot-console>" --headless --scene res://scenes/tournament_runner.tscn &

# Trigger a tournament run (server must be running)
curl -s -X POST -H "Content-Length: 0" http://localhost:8585/api/tournament/run

# Kill the server (platform-specific)
# Windows: taskkill //F //IM godot_console.exe
# Linux/Mac: pkill -f godot

# Clear fight data (delete all generations)
rm -rf generations/gen_*
```

**Notes:**
- The server binds port 8585; if a stale process holds it, kill the old process first
- After killing, wait ~2s before restarting to let the port release
- The server does NOT auto-exit after a tournament; it stays up to serve the dashboard and replay viewer

## Controls (main scene)
- Space: pause/unpause
- R: restart match
- N: next matchup

## Workflow Preferences
- **Python**: Always write `.py` files instead of inline `python3 -c` one-liners in bash. Never use `python3 -c`. Save scripts to `tools/` and run them.
