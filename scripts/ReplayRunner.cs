// ─────────────────────────────────────────────────────────────────────────────
// ReplayRunner.cs — Replays a match from battle JSON with divergence detection
// ─────────────────────────────────────────────────────────────────────────────
//
// Supports both Fight mode and Beacon Brawl mode.
// Reads replay_config.json from the project root to find the battle log path.
// Re-simulates the match tick-by-tick using the stored seed + BT trees, then
// compares live state against stored checkpoints to detect determinism drift.
//
// Controls:
//   Space       — Pause / unpause
//   Left/Right  — Step one tick (while paused)
//   R           — Restart replay
//   Escape      — Quit

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;
using AiBtGym.Simulation.BeaconBrawl;
using AiBtGym.Godot.BeaconBrawl;

namespace AiBtGym.Godot;

public partial class ReplayRunner : Node2D
{
    // ── Camera ──
    private Camera2D? _camera;
    private bool _dragging;
    private Vector2 _dragStart;
    private const float ZoomMin = 0.25f;
    private const float ZoomMax = 3f;
    private const float ZoomStep = 0.1f;

    // ── Shared state ──
    private Label? _statusLabel;
    private bool _paused;
    private bool _diverged;
    private int _firstDivergenceTick = -1;
    private int _checkpointsVerified;
    private int _checkpointsFailed;
    private List<string> _divergenceLog = [];
    private bool _summaryPrinted;
    private bool _isBeaconBrawl;
    private bool _hasMatch; // true once a match has been set up (either mode)

    // Tolerances for float comparison
    private const float PosTolerance = 0.01f;
    private const float VelTolerance = 0.1f;
    private const float HpTolerance = 0.01f;
    private const float ChainTolerance = 0.01f;

    // ── Fight mode state ──
    private Match? _fightMatch;
    private ReplayData? _fightReplayData;
    private Dictionary<int, ReplayCheckpoint> _fightCheckpoints = new();

    // ── Beacon Brawl state ──
    private BeaconMatch? _beaconMatch;
    private BeaconReplayData? _beaconReplayData;
    private Dictionary<int, BeaconReplayCheckpoint> _beaconCheckpoints = new();

    public override void _Ready()
    {
        // Read config to find battle log path
        string configPath = ProjectSettings.GlobalizePath("res://replay_config.json");
        if (!File.Exists(configPath))
        {
            ShowError("replay_config.json not found — cannot start replay");
            return;
        }

        string configJson = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ReplayConfig>(configJson, TournamentJson.Options);
        if (config?.BattleLogPath == null)
        {
            ShowError("replay_config.json missing battle_log_path");
            return;
        }

        string battlePath = config.BattleLogPath;
        if (!File.Exists(battlePath))
        {
            ShowError($"Battle log not found:\n{battlePath}");
            return;
        }

        string battleJson = File.ReadAllText(battlePath);

        // Detect mode: check if the JSON has "team_a_trees" in the replay (beacon brawl)
        // or "fighter_trees" (fight mode)
        using var doc = JsonDocument.Parse(battleJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("replay", out var replayEl) &&
            replayEl.TryGetProperty("team_a_trees", out _))
        {
            _isBeaconBrawl = true;
            LoadBeaconBrawl(battleJson);
        }
        else
        {
            _isBeaconBrawl = false;
            LoadFightMode(battleJson);
        }
    }

    // ── Fight mode loading ──

    private void LoadFightMode(string battleJson)
    {
        var battleLog = JsonSerializer.Deserialize<BattleLog>(battleJson, TournamentJson.Options);
        _fightReplayData = battleLog?.Replay;

        if (_fightReplayData == null || _fightReplayData.FighterTrees.Count < 2)
        {
            ShowError("Battle log has no replay data or missing fighter trees.");
            return;
        }

        foreach (var cp in _fightReplayData.Checkpoints)
            _fightCheckpoints[cp.T] = cp;

        GD.Print($"=== REPLAY (Fight): {battleLog!.Fighter} vs {battleLog.Opponent} ===");
        GD.Print($"  Seed: {_fightReplayData.MatchSeed?.ToString() ?? "NONE"}");
        GD.Print($"  Checkpoints: {_fightReplayData.Checkpoints.Count}");

        StartReplay();
    }

    // ── Beacon Brawl loading ──

    private void LoadBeaconBrawl(string battleJson)
    {
        var battleLog = JsonSerializer.Deserialize<BeaconBattleLog>(battleJson, TournamentJson.Options);
        _beaconReplayData = battleLog?.Replay;

        if (_beaconReplayData?.TeamATrees == null || _beaconReplayData?.TeamBTrees == null)
        {
            ShowError("Beacon Brawl battle log has no replay trees.");
            return;
        }

        foreach (var cp in _beaconReplayData.Checkpoints)
            _beaconCheckpoints[cp.T] = cp;

        GD.Print($"=== REPLAY (Beacon Brawl): {battleLog!.Team} vs {battleLog.Opponent} ===");
        GD.Print($"  Seed: {_beaconReplayData.MatchSeed?.ToString() ?? "NONE (pre-seed era)"}");
        GD.Print($"  Checkpoints: {_beaconReplayData.Checkpoints.Count}");

        StartReplay();
    }

    // ── Start / restart ──

    private void StartReplay()
    {
        // Clear state
        _diverged = false;
        _firstDivergenceTick = -1;
        _checkpointsVerified = 0;
        _checkpointsFailed = 0;
        _divergenceLog.Clear();
        _summaryPrinted = false;
        _hasMatch = false;

        foreach (var child in GetChildren())
            child.QueueFree();

        // Camera for pan/zoom (middle-click drag, scroll wheel)
        _camera = new Camera2D { Enabled = true };
        AddChild(_camera);

        if (_isBeaconBrawl)
            StartBeaconReplay();
        else
            StartFightReplay();

        FitCameraToArena();
    }

    private void StartFightReplay()
    {
        if (_fightReplayData == null) return;

        var arenaData = _fightReplayData.Arena;
        var arena = new Arena(arenaData.Width, arenaData.Height);
        var tree0 = _fightReplayData.FighterTrees[0];
        var tree1 = _fightReplayData.FighterTrees[1];
        _fightMatch = new Match(arena, tree0, tree1, seed: _fightReplayData.MatchSeed);

        // Arena renderer
        AddChild(new ArenaRenderer { Arena = arena });

        // Fighter renderers
        AddChild(new FighterRenderer
        {
            Fighter = _fightMatch.Fighter0,
            TeamColor = new Color(0.9f, 0.2f, 0.2f)
        });
        AddChild(new FighterRenderer
        {
            Fighter = _fightMatch.Fighter1,
            TeamColor = new Color(0.3f, 0.5f, 1f)
        });

        // Debug overlay
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);
        canvasLayer.AddChild(new DebugOverlay { Match = _fightMatch });

        AddStatusLabel(canvasLayer, arenaData.Height);
        _hasMatch = true;
    }

    private void StartBeaconReplay()
    {
        if (_beaconReplayData == null) return;

        var ad = _beaconReplayData.Arena;
        var arena = new BeaconArena(ad.Width, ad.Height);

        // Extract roles from the first checkpoint's pawn data
        var firstCp = _beaconReplayData.Checkpoints[0];
        int teamSize = _beaconReplayData.TeamSize;
        var rolesA = firstCp.P.Where(p => p.T == 0).Select(p => (PawnRole)p.R).ToArray();
        var rolesB = firstCp.P.Where(p => p.T == 1).Select(p => (PawnRole)p.R).ToArray();

        _beaconMatch = new BeaconMatch(arena,
            _beaconReplayData.TeamATrees!.ToArray(), rolesA,
            _beaconReplayData.TeamBTrees!.ToArray(), rolesB,
            seed: _beaconReplayData.MatchSeed);

        // Colors — parse from battle log or use defaults
        var colorA = new Color(0.16f, 0.5f, 0.73f);  // blue
        var colorB = new Color(0.56f, 0.27f, 0.68f);  // purple

        // Arena renderer
        AddChild(new BeaconArenaRenderer
        {
            Arena = arena,
            Beacons = _beaconMatch.Beacons,
            TeamAColor = colorA,
            TeamBColor = colorB
        });

        // Pawn renderers
        foreach (var pawn in _beaconMatch.AllPawns)
        {
            AddChild(new PawnRenderer
            {
                Pawn = pawn,
                TeamColor = pawn.TeamIndex == 0 ? colorA : colorB
            });
        }

        // Projectile renderer + wire rifle flash notifications
        var projRenderer = new ProjectileRenderer
        {
            Match = _beaconMatch,
            TeamAColor = colorA,
            TeamBColor = colorB
        };
        AddChild(projRenderer);
        _beaconMatch.OnRifleFired = (segments, team) =>
            projRenderer.NotifyRifleShot(segments, team);

        // Score overlay
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);
        canvasLayer.AddChild(new ScoreOverlay
        {
            Match = _beaconMatch,
            TeamAColor = colorA,
            TeamBColor = colorB,
            TeamAName = "Team A",
            TeamBName = "Team B"
        });

        AddStatusLabel(canvasLayer, ad.Height);
        _hasMatch = true;
    }

    private void FitCameraToArena()
    {
        if (_camera == null) return;

        float arenaW, arenaH;
        if (_isBeaconBrawl && _beaconReplayData != null)
        {
            arenaW = _beaconReplayData.Arena.Width;
            arenaH = _beaconReplayData.Arena.Height;
        }
        else if (_fightReplayData != null)
        {
            arenaW = _fightReplayData.Arena.Width;
            arenaH = _fightReplayData.Arena.Height;
        }
        else return;

        // Center camera on arena
        _camera.Position = new Vector2(arenaW / 2f, arenaH / 2f);

        // Zoom to fit arena in viewport with some padding
        var viewport = GetViewportRect().Size;
        if (viewport.X <= 0 || viewport.Y <= 0) return;
        float zoomX = viewport.X / (arenaW + 40f);
        float zoomY = viewport.Y / (arenaH + 40f);
        float zoom = Mathf.Min(zoomX, zoomY);
        zoom = Mathf.Clamp(zoom, ZoomMin, ZoomMax);
        _camera.Zoom = new Vector2(zoom, zoom);
    }

    private void AddStatusLabel(CanvasLayer layer, float arenaHeight)
    {
        _statusLabel = new Label
        {
            Position = new Vector2(20, arenaHeight - 40),
            Text = "REPLAY — Verifying..."
        };
        _statusLabel.AddThemeColorOverride("font_color", Colors.LimeGreen);
        _statusLabel.AddThemeFontSizeOverride("font_size", 18);
        layer.AddChild(_statusLabel);
    }

    // ── Simulation loop ──

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasMatch) return;

        bool isOver = _isBeaconBrawl ? (_beaconMatch?.IsOver ?? true) : (_fightMatch?.IsOver ?? true);
        if (isOver) { PrintSummaryOnce(); return; }
        if (_paused) return;

        StepOne();
    }

    private void StepOne()
    {
        if (_isBeaconBrawl)
        {
            if (_beaconMatch == null || _beaconMatch.IsOver) return;
            _beaconMatch.Step();
            CheckBeaconDivergence(_beaconMatch.Tick - 1);
            if (_beaconMatch.IsOver) PrintSummaryOnce();
        }
        else
        {
            if (_fightMatch == null || _fightMatch.IsOver) return;
            _fightMatch.Step();
            CheckFightDivergence(_fightMatch.Tick - 1);
            if (_fightMatch.IsOver) PrintSummaryOnce();
        }
    }

    // ── Fight mode divergence ──

    private void CheckFightDivergence(int tick)
    {
        if (_fightMatch == null) return;
        if (!_fightCheckpoints.TryGetValue(tick, out var expected)) return;

        var errors = new List<string>();
        CheckFighter(errors, 0, _fightMatch.Fighter0, expected.F[0]);
        CheckFighter(errors, 1, _fightMatch.Fighter1, expected.F[1]);
        CheckFist(errors, "F0.L", _fightMatch.Fighter0.LeftFist, expected.Fists[0]);
        CheckFist(errors, "F0.R", _fightMatch.Fighter0.RightFist, expected.Fists[1]);
        CheckFist(errors, "F1.L", _fightMatch.Fighter1.LeftFist, expected.Fists[2]);
        CheckFist(errors, "F1.R", _fightMatch.Fighter1.RightFist, expected.Fists[3]);

        RecordCheckResult(tick, errors);
    }

    // ── Beacon Brawl divergence ──

    private void CheckBeaconDivergence(int tick)
    {
        if (_beaconMatch == null) return;
        if (!_beaconCheckpoints.TryGetValue(tick, out var expected)) return;

        var errors = new List<string>();

        // Scores
        if (_beaconMatch.Scores[0] != expected.S[0])
            errors.Add($"Score[0]: expected {expected.S[0]}, got {_beaconMatch.Scores[0]}");
        if (_beaconMatch.Scores[1] != expected.S[1])
            errors.Add($"Score[1]: expected {expected.S[1]}, got {_beaconMatch.Scores[1]}");

        // Pawns
        for (int i = 0; i < _beaconMatch.AllPawns.Length && i < expected.P.Count; i++)
        {
            var pawn = _beaconMatch.AllPawns[i];
            var exp = expected.P[i];
            string name = $"Pawn[{pawn.TeamIndex}.{pawn.PawnIndex}]";

            if (MathF.Abs(pawn.Position.X - exp.X) > PosTolerance)
                errors.Add($"{name}.X: expected {exp.X:F4}, got {pawn.Position.X:F4}");
            if (MathF.Abs(pawn.Position.Y - exp.Y) > PosTolerance)
                errors.Add($"{name}.Y: expected {exp.Y:F4}, got {pawn.Position.Y:F4}");
            if (MathF.Abs(pawn.Velocity.X - exp.Vx) > VelTolerance)
                errors.Add($"{name}.Vx: expected {exp.Vx:F4}, got {pawn.Velocity.X:F4}");
            if (MathF.Abs(pawn.Velocity.Y - exp.Vy) > VelTolerance)
                errors.Add($"{name}.Vy: expected {exp.Vy:F4}, got {pawn.Velocity.Y:F4}");
            if (MathF.Abs(pawn.Health - exp.Hp) > HpTolerance)
                errors.Add($"{name}.Hp: expected {exp.Hp:F4}, got {pawn.Health:F4}");
            if (pawn.IsGrounded != exp.G)
                errors.Add($"{name}.Grounded: expected {exp.G}, got {pawn.IsGrounded}");
            if (pawn.IsDead != exp.D)
                errors.Add($"{name}.Dead: expected {exp.D}, got {pawn.IsDead}");
        }

        // Hooks (grappler fists)
        for (int i = 0; i < expected.F.Count; i++)
        {
            var expHook = expected.F[i];
            if (expHook.Pi < 0 || expHook.Pi >= _beaconMatch.AllPawns.Length) continue;
            var pawn = _beaconMatch.AllPawns[expHook.Pi];
            if (pawn.Role != PawnRole.Grappler) continue;
            var hook = pawn.Hook;
            string name = $"Hook[{pawn.TeamIndex}.{pawn.PawnIndex}]";

            if ((int)hook.ChainState != expHook.S)
                errors.Add($"{name}.State: expected {expHook.S}, got {(int)hook.ChainState}");
            if (MathF.Abs(hook.Position.X - expHook.X) > PosTolerance)
                errors.Add($"{name}.X: expected {expHook.X:F4}, got {hook.Position.X:F4}");
            if (MathF.Abs(hook.Position.Y - expHook.Y) > PosTolerance)
                errors.Add($"{name}.Y: expected {expHook.Y:F4}, got {hook.Position.Y:F4}");
            if (hook.IsAttachedToWorld != expHook.A)
                errors.Add($"{name}.Attached: expected {expHook.A}, got {hook.IsAttachedToWorld}");
        }

        // Beacons
        for (int i = 0; i < _beaconMatch.Beacons.Length && i < expected.B.Count; i++)
        {
            var beacon = _beaconMatch.Beacons[i];
            var exp = expected.B[i];
            if (beacon.OwnerTeam != exp.O)
                errors.Add($"Beacon[{i}].Owner: expected {exp.O}, got {beacon.OwnerTeam}");
        }

        RecordCheckResult(tick, errors);
    }

    // ── Shared check helpers ──

    private void CheckFighter(List<string> errors, int idx, Fighter actual, ReplayFighter expected)
    {
        string prefix = $"Fighter{idx}";
        if (MathF.Abs(actual.Position.X - expected.X) > PosTolerance)
            errors.Add($"{prefix}.X: expected {expected.X:F4}, got {actual.Position.X:F4}");
        if (MathF.Abs(actual.Position.Y - expected.Y) > PosTolerance)
            errors.Add($"{prefix}.Y: expected {expected.Y:F4}, got {actual.Position.Y:F4}");
        if (MathF.Abs(actual.Velocity.X - expected.Vx) > VelTolerance)
            errors.Add($"{prefix}.Vx: expected {expected.Vx:F4}, got {actual.Velocity.X:F4}");
        if (MathF.Abs(actual.Velocity.Y - expected.Vy) > VelTolerance)
            errors.Add($"{prefix}.Vy: expected {expected.Vy:F4}, got {actual.Velocity.Y:F4}");
        if (MathF.Abs(actual.Health - expected.Hp) > HpTolerance)
            errors.Add($"{prefix}.Hp: expected {expected.Hp:F4}, got {actual.Health:F4}");
        if (actual.IsGrounded != expected.G)
            errors.Add($"{prefix}.Grounded: expected {expected.G}, got {actual.IsGrounded}");
    }

    private void CheckFist(List<string> errors, string name, Fist actual, ReplayFist expected)
    {
        if ((int)actual.ChainState != expected.S)
            errors.Add($"{name}.ChainState: expected {expected.S}, got {(int)actual.ChainState}");
        if (MathF.Abs(actual.Position.X - expected.X) > PosTolerance)
            errors.Add($"{name}.X: expected {expected.X:F4}, got {actual.Position.X:F4}");
        if (MathF.Abs(actual.Position.Y - expected.Y) > PosTolerance)
            errors.Add($"{name}.Y: expected {expected.Y:F4}, got {actual.Position.Y:F4}");
        if (MathF.Abs(actual.AnchorPoint.X - expected.Ax) > ChainTolerance)
            errors.Add($"{name}.Ax: expected {expected.Ax:F4}, got {actual.AnchorPoint.X:F4}");
        if (MathF.Abs(actual.AnchorPoint.Y - expected.Ay) > ChainTolerance)
            errors.Add($"{name}.Ay: expected {expected.Ay:F4}, got {actual.AnchorPoint.Y:F4}");
        if (MathF.Abs(actual.ChainLength - expected.Cl) > ChainTolerance)
            errors.Add($"{name}.ChainLen: expected {expected.Cl:F4}, got {actual.ChainLength:F4}");
        if (actual.IsAttachedToWorld != expected.A)
            errors.Add($"{name}.Attached: expected {expected.A}, got {actual.IsAttachedToWorld}");
    }

    private void RecordCheckResult(int tick, List<string> errors)
    {
        if (errors.Count > 0)
        {
            _checkpointsFailed++;
            if (!_diverged)
            {
                _diverged = true;
                _firstDivergenceTick = tick;
                GD.PrintErr($"!!! DIVERGENCE DETECTED at tick {tick} !!!");
            }
            foreach (var err in errors)
            {
                GD.PrintErr($"  [{tick}] {err}");
                _divergenceLog.Add($"[{tick}] {err}");
            }
            UpdateStatusLabel();
        }
        else
        {
            _checkpointsVerified++;
        }
    }

    // ── UI ──

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null) return;
        if (_diverged)
        {
            _statusLabel.Text = $"DIVERGED at tick {_firstDivergenceTick}  |  {_checkpointsFailed} checkpoints failed";
            _statusLabel.RemoveThemeColorOverride("font_color");
            _statusLabel.AddThemeColorOverride("font_color", Colors.Red);
        }
    }

    private void PrintSummaryOnce()
    {
        if (_summaryPrinted) return;
        _summaryPrinted = true;

        int total = _checkpointsVerified + _checkpointsFailed;
        string mode = _isBeaconBrawl ? "Beacon Brawl" : "Fight";
        GD.Print($"=== REPLAY COMPLETE ({mode}) ===");
        GD.Print($"  Checkpoints: {total} total, {_checkpointsVerified} passed, {_checkpointsFailed} failed");

        if (_diverged)
        {
            GD.PrintErr($"  RESULT: DIVERGED (first at tick {_firstDivergenceTick})");
            GD.PrintErr($"  Errors logged: {_divergenceLog.Count}");
        }
        else
        {
            GD.Print("  RESULT: DETERMINISTIC — all checkpoints match");
        }

        if (_statusLabel != null && !_diverged)
        {
            _statusLabel.Text = $"DETERMINISTIC — {total} checkpoints verified";
            _statusLabel.RemoveThemeColorOverride("font_color");
            _statusLabel.AddThemeColorOverride("font_color", Colors.LimeGreen);
        }
    }

    private void ShowError(string message)
    {
        GD.PrintErr(message);

        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);

        var bg = new ColorRect
        {
            Color = new Color(0.05f, 0.07f, 0.09f),
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        canvasLayer.AddChild(bg);

        var label = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect
        };
        label.AddThemeColorOverride("font_color", new Color(0.97f, 0.32f, 0.29f));
        label.AddThemeFontSizeOverride("font_size", 20);
        canvasLayer.AddChild(label);

        var hint = new Label
        {
            Text = "Press Escape to close",
            Position = new Vector2(0, 600),
            HorizontalAlignment = HorizontalAlignment.Center,
            Size = new Vector2(1500, 40)
        };
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.62f));
        hint.AddThemeFontSizeOverride("font_size", 14);
        canvasLayer.AddChild(hint);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // ── Mouse pan & zoom ──
        if (@event is InputEventMouseButton mb && _camera != null)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Middle:
                    _dragging = mb.Pressed;
                    if (mb.Pressed)
                        _dragStart = mb.GlobalPosition;
                    break;
                case MouseButton.WheelUp:
                    if (mb.Pressed)
                    {
                        var z = _camera.Zoom;
                        _camera.Zoom = new Vector2(
                            Mathf.Clamp(z.X + ZoomStep, ZoomMin, ZoomMax),
                            Mathf.Clamp(z.Y + ZoomStep, ZoomMin, ZoomMax));
                    }
                    break;
                case MouseButton.WheelDown:
                    if (mb.Pressed)
                    {
                        var z = _camera.Zoom;
                        _camera.Zoom = new Vector2(
                            Mathf.Clamp(z.X - ZoomStep, ZoomMin, ZoomMax),
                            Mathf.Clamp(z.Y - ZoomStep, ZoomMin, ZoomMax));
                    }
                    break;
            }
        }

        if (@event is InputEventMouseMotion mm && _dragging && _camera != null)
        {
            _camera.Position -= mm.Relative / _camera.Zoom;
        }

        // ── Keyboard ──
        if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Space when _hasMatch:
                    _paused = !_paused;
                    GD.Print(_paused ? "PAUSED" : "RUNNING");
                    break;
                case Key.Right when _paused && _hasMatch:
                    StepOne();
                    break;
                case Key.Left when _paused && _hasMatch:
                    GD.Print("(Left = restart from beginning)");
                    StartReplay();
                    break;
                case Key.R when _hasMatch:
                    StartReplay();
                    break;
                case Key.Home when _camera != null:
                    FitCameraToArena();
                    break;
                case Key.Escape:
                    GetTree().Quit();
                    break;
            }
        }
    }
}

// Config file model
internal record ReplayConfig
{
    public string? BattleLogPath { get; init; }
}
