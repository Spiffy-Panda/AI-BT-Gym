# CLAUDE.md

## Project
AI-BT-Gym — Evolutionary behavior tree gym in Godot 4.6 (C#).

## Godot
- **Godot executable**: `"/c/Program Files/Godot/Godot_console.exe"` (headless-capable)
- **Editor**: `"/c/Program Files/Godot/Godot.exe"`
- **Version**: 4.6.1 stable mono

## Build & Test
```bash
# Build
dotnet build

# Run tests headlessly
"/c/Program Files/Godot/Godot_console.exe" --headless --scene res://scenes/test_runner.tscn

# Run the game
"/c/Program Files/Godot/Godot_console.exe" --scene res://scenes/main.tscn
```

## Key Paths
- Simulation core: `simulation/`
- BT system: `simulation/BehaviorTree/`
- Godot scripts: `scripts/`
- Tests: `tests/MovementTests.cs` (run via `scenes/test_runner.tscn`)
- Test BTs: `scripts/TestTrees.cs`
- Seed AI definitions: `seed_generation/` (`SeedTrees.cs`, `Gen00xTrees.cs`, `SubTrees.cs`)
- Tournament output: `tournaments/` (e.g. `tournaments/beacon_brawl/gen_000/`)
- Analysis scripts: `tools/`

## Tournament Server
- Scene: `scenes/tournament_runner.tscn`
- Script: `scripts/TournamentRunner.cs`
- URL: http://localhost:8585 (port configurable via `PORT` env var)
- The server runs inside Godot headless and stays alive to serve results

```bash
# Cycle server (kill → restart, waits for ready)
tools/server.sh restart

# Cycle server AND clear all generation data
tools/server.sh restart-clean

# Stop server
tools/server.sh stop

# Check server status
tools/server.sh status

# Trigger a tournament run (server must be running)
curl -s -X POST -H "Content-Length: 0" http://localhost:8585/api/tournament/run
```

## Controls (main scene)
- Space: pause/unpause
- R: restart match
- N: next matchup

## Workflow Preferences
- **Python**: Always write `.py` files instead of inline `python3 -c` one-liners in bash. Never use `python3 -c`. Save scripts to `tools/` and run them.
