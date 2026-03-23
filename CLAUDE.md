# CLAUDE.md

## Project
AI-BT-Gym — Evolutionary behavior tree gym in Godot 4.6 (C#).

## Godot
- **Godot executable**: `"/c/Program Files/godot/godot_console.exe"` (headless-capable)
- **Editor**: `"/c/Program Files/godot/godot.exe"`
- **Version**: 4.6.1 stable mono

## Build & Test
```bash
# Build
dotnet build

# Run tests headlessly
"/c/Program Files/godot/godot_console.exe" --headless --scene res://scenes/test_runner.tscn

# Run the game
"/c/Program Files/godot/godot_console.exe" --scene res://scenes/main.tscn
```

## Key Paths
- Simulation core: `simulation/`
- BT system: `simulation/BehaviorTree/`
- Godot scripts: `scripts/`
- Tests: `tests/MovementTests.cs` (run via `scenes/test_runner.tscn`)
- Test BTs: `scripts/TestTrees.cs`

## Controls (main scene)
- Space: pause/unpause
- R: restart match
- N: next matchup
