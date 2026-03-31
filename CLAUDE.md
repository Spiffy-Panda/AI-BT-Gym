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
- BT system (shared): `simulation/BehaviorTree/`
- Shared tournament infra: `simulation/{Tournament,TournamentModels,EloCalculator}.cs`
- Shared scripts: `scripts/{TournamentRunner,TournamentRegistry,ReplayRunner}.cs`, `scripts/web/`
- Tests: `tests/` (run via `scenes/test_runner.tscn`)
- Tournament output: `tournaments/` (e.g. `tournaments/beacon_brawl/gen_000/`)

### Game Modes (scope searches with `*/beacon_brawl/` or `*/dueling_fighters/`)
- **Beacon Brawl** (team capture-the-beacon):
  - Simulation: `simulation/beacon_brawl/`
  - Rendering: `scripts/beacon_brawl/`
  - Seed AIs: `seed_generation/beacon_brawl/`
  - Scene: `scenes/beacon_brawl.tscn`
  - Tools: `tools/beacon_brawl/`
- **Dueling Fighters** (1v1 melee):
  - Simulation: `simulation/dueling_fighters/`
  - Rendering: `scripts/dueling_fighters/`
  - Seed AIs: `seed_generation/dueling_fighters/`
  - Scene: `scenes/main.tscn`
  - Tools: `tools/` (root-level scripts)

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
