// ─────────────────────────────────────────────────────────────────────────────
// ReplayRunner.cs — Replays a match from battle JSON with divergence detection
// ─────────────────────────────────────────────────────────────────────────────
//
// Pre-simulates the entire match at startup, capturing per-tick snapshots.
// Playback and scrubbing are instant — just index into the snapshot array.
// Divergence checking runs during pre-simulation against stored checkpoints.
//
// Controls:
//   Space       — Pause / unpause
//   Left/Right  — Step one tick (while paused)
//   R           — Restart replay
//   Home        — Reset camera
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
    private const float ZoomMin = 0.25f;
    private const float ZoomMax = 3f;
    private const float ZoomStep = 0.1f;

    // ── HUD controls ──
    private Label? _tickLabel;
    private Label? _divergenceLabel;
    private Button? _playPauseBtn;
    private HSlider? _scrubBar;
    private bool _scrubbing;

    // ── Playback state ──
    private bool _paused;
    private bool _isBeaconBrawl;
    private bool _hasMatch;
    private int _displayTick;
    private int _finalTick; // last tick with a snapshot
    private List<TickSnapshot> _timeline = [];

    // ── Divergence results (computed during pre-sim) ──
    private bool _diverged;
    private int _firstDivergenceTick = -1;
    private int _checkpointsVerified;
    private int _checkpointsFailed;
    private List<string> _divergenceLog = [];

    // Tolerances
    private const float PosTolerance = 0.01f;
    private const float VelTolerance = 0.1f;
    private const float HpTolerance = 0.01f;
    private const float ChainTolerance = 0.01f;

    // ── Replay metadata (for screenshots) ──
    private string _matchName = "";
    private string _battleLogPath = "";

    // ── Replay data ──
    private ReplayData? _fightReplayData;
    private Dictionary<int, ReplayCheckpoint> _fightCheckpoints = new();
    private BeaconReplayData? _beaconReplayData;
    private Dictionary<int, BeaconReplayCheckpoint> _beaconCheckpoints = new();

    // ── Live objects (renderers read from these) ──
    // Fight mode
    private Fighter? _fighter0, _fighter1;
    private Match? _replayMatch; // holds mutable feature state for ArenaRenderer
    // Beacon mode
    private Pawn[]? _pawns;
    private Beacon[]? _beacons;
    private int[]? _scores;
    private int[]? _kills;
    private BeaconMatch? _beaconMatchRef; // for score overlay
    private ProjectileRenderer? _projectileRenderer;

    // ═══════════════════════════════════════════════════════════════════════
    // Snapshot types — lightweight per-tick state
    // ═══════════════════════════════════════════════════════════════════════

    private struct TickSnapshot
    {
        // Fight mode (2 fighters, 4 fists)
        public FighterSnap[]? Fighters; // [2]
        public FistSnap[]? Fists;       // [4]: L0, R0, L1, R1

        // Feature state (fight mode, for arena renderer)
        public float[]? WallHp;
        public bool[]? WallExists;
        public bool[]? PickupActive;
        public float EffectiveLeft, EffectiveRight;

        // Beacon mode
        public PawnSnap[]? Pawns;       // [N]
        public BeaconSnap[]? Beacons;   // [3]
        public int[]? Scores;           // [2]
        public int[]? Kills;            // [2]
        public int[]? Rates;            // [2]
        public ProjectileSnap[]? Projectiles; // live pistol bullets
        public RifleFlashSnap[]? RifleFlashes; // rifle shots fired this tick
        public bool IsOver;
        public int WinnerIdx;
    }

    private struct ProjectileSnap
    {
        public float X, Y, Vx, Vy, Gravity, Damage, Knockback, Radius;
        public int OwnerTeam, OwnerPawnIndex, LifetimeRemaining;
        public bool IsAlive;
    }

    private struct RifleFlashSnap
    {
        public Vector2[] Segments;
        public int Team;
    }

    private struct FighterSnap
    {
        public float X, Y, Vx, Vy, Hp;
        public bool Grounded;
    }

    private struct FistSnap
    {
        public int State; // FistChainState
        public float X, Y, Ax, Ay, Cl, Dx, Dy;
        public bool Attached;
    }

    private struct PawnSnap
    {
        public float X, Y, Vx, Vy, Hp;
        public bool Grounded, Dead, Stunned, Vulnerable, ParryActive;
        public int StunTicks, VulnTicks, ParryCooldown, ParryActiveTicks;
        public int RespawnTimer;
        // Hook (for grapplers)
        public int HookState;
        public float HookX, HookY, HookAx, HookAy, HookCl;
        public bool HookAttached;
    }

    private struct BeaconSnap
    {
        public int Owner, CaptureProgress;
        public bool Contested;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Initialization
    // ═══════════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        string configPath = ProjectSettings.GlobalizePath("res://replay_config.json");
        if (!File.Exists(configPath)) { ShowError("replay_config.json not found"); return; }

        string configJson = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<ReplayConfig>(configJson, TournamentJson.Options);
        if (config?.BattleLogPath == null) { ShowError("replay_config.json missing battle_log_path"); return; }

        string battlePath = config.BattleLogPath;
        if (!File.Exists(battlePath)) { ShowError($"Battle log not found:\n{battlePath}"); return; }
        _battleLogPath = battlePath;

        string battleJson = File.ReadAllText(battlePath);

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

    private void LoadFightMode(string battleJson)
    {
        var battleLog = JsonSerializer.Deserialize<BattleLog>(battleJson, TournamentJson.Options);
        _fightReplayData = battleLog?.Replay;
        if (_fightReplayData == null || _fightReplayData.FighterTrees.Count < 2)
        { ShowError("No replay data or missing fighter trees."); return; }

        foreach (var cp in _fightReplayData.Checkpoints)
            _fightCheckpoints[cp.T] = cp;

        _matchName = $"{battleLog!.Fighter} vs {battleLog.Opponent}";
        GD.Print($"=== REPLAY (Fight): {_matchName} ===");
        GD.Print($"  Seed: {_fightReplayData.MatchSeed?.ToString() ?? "NONE"}");
        StartReplay();
    }

    private void LoadBeaconBrawl(string battleJson)
    {
        var battleLog = JsonSerializer.Deserialize<BeaconBattleLog>(battleJson, TournamentJson.Options);
        _beaconReplayData = battleLog?.Replay;
        if (_beaconReplayData?.TeamATrees == null || _beaconReplayData?.TeamBTrees == null)
        { ShowError("Beacon Brawl battle log has no replay trees."); return; }

        foreach (var cp in _beaconReplayData.Checkpoints)
            _beaconCheckpoints[cp.T] = cp;

        _matchName = $"{battleLog!.Team} vs {battleLog.Opponent}";
        GD.Print($"=== REPLAY (Beacon Brawl): {_matchName} ===");
        GD.Print($"  Seed: {_beaconReplayData.MatchSeed?.ToString() ?? "NONE"}");
        StartReplay();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pre-simulation + scene setup
    // ═══════════════════════════════════════════════════════════════════════

    private void StartReplay()
    {
        _diverged = false;
        _firstDivergenceTick = -1;
        _checkpointsVerified = 0;
        _checkpointsFailed = 0;
        _divergenceLog.Clear();
        _displayTick = 0;
        _hasMatch = false;
        _paused = false;

        foreach (var child in GetChildren())
            child.QueueFree();

        _camera = new Camera2D { Enabled = true };
        AddChild(_camera);

        // Pre-simulate and build timeline
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _timeline.Clear();

        if (_isBeaconBrawl)
            PreSimulateBeacon();
        else
            PreSimulateFight();

        sw.Stop();
        _finalTick = _timeline.Count - 1;
        GD.Print($"  Pre-simulated {_timeline.Count} ticks in {sw.ElapsedMilliseconds}ms");
        GD.Print($"  Divergence: {(_diverged ? $"DIVERGED at tick {_firstDivergenceTick}" : "DETERMINISTIC")}");
        GD.Print($"  Checkpoints: {_checkpointsVerified} passed, {_checkpointsFailed} failed");

        // Build scene (renderers bound to live objects we'll update each frame)
        if (_isBeaconBrawl)
            BuildBeaconScene();
        else
            BuildFightScene();

        FitCameraToArena();
        ApplySnapshot(_displayTick);
    }

    // ── Fight pre-sim ──

    private void PreSimulateFight()
    {
        var rd = _fightReplayData!;
        // Use the full arena config from replay data (includes platforms, hazards, walls, etc.)
        var arena = rd.Arena.Config != null ? new Arena(rd.Arena.Config) : new Arena(rd.Arena.Width, rd.Arena.Height);
        var match = new Match(arena, rd.FighterTrees[0], rd.FighterTrees[1], seed: rd.MatchSeed);

        // Capture initial state (tick 0, before any step — NOT a checkpoint)
        // First step will produce tick 0 checkpoint
        while (!match.IsOver)
        {
            match.Step();
            int justCompleted = match.Tick - 1;

            // Capture snapshot
            _timeline.Add(CaptureFightSnapshot(match));

            // Divergence check against stored checkpoints
            if (_fightCheckpoints.TryGetValue(justCompleted, out var expected))
            {
                var errors = new List<string>();
                CheckFighter(errors, 0, match.Fighter0, expected.F[0]);
                CheckFighter(errors, 1, match.Fighter1, expected.F[1]);
                CheckFist(errors, "F0.L", match.Fighter0.LeftFist, expected.Fists[0]);
                CheckFist(errors, "F0.R", match.Fighter0.RightFist, expected.Fists[1]);
                CheckFist(errors, "F1.L", match.Fighter1.LeftFist, expected.Fists[2]);
                CheckFist(errors, "F1.R", match.Fighter1.RightFist, expected.Fists[3]);
                RecordCheckResult(justCompleted, errors);
            }
        }

        // Create the live fighter objects for renderers
        _fighter0 = new Fighter(0, new Vector2(0, 0));
        _fighter1 = new Fighter(1, new Vector2(0, 0));
    }

    private TickSnapshot CaptureFightSnapshot(Match m) => new()
    {
        Fighters =
        [
            new FighterSnap { X = m.Fighter0.Position.X, Y = m.Fighter0.Position.Y,
                Vx = m.Fighter0.Velocity.X, Vy = m.Fighter0.Velocity.Y,
                Hp = m.Fighter0.Health, Grounded = m.Fighter0.IsGrounded },
            new FighterSnap { X = m.Fighter1.Position.X, Y = m.Fighter1.Position.Y,
                Vx = m.Fighter1.Velocity.X, Vy = m.Fighter1.Velocity.Y,
                Hp = m.Fighter1.Health, Grounded = m.Fighter1.IsGrounded }
        ],
        Fists =
        [
            CapFist(m.Fighter0.LeftFist), CapFist(m.Fighter0.RightFist),
            CapFist(m.Fighter1.LeftFist), CapFist(m.Fighter1.RightFist)
        ],
        // Feature state
        WallHp = m.DestructibleWallHp.Length > 0 ? (float[])m.DestructibleWallHp.Clone() : null,
        WallExists = m.DestructibleWallExists.Length > 0 ? (bool[])m.DestructibleWallExists.Clone() : null,
        PickupActive = m.PickupActive.Length > 0 ? (bool[])m.PickupActive.Clone() : null,
        EffectiveLeft = m.EffectiveLeft,
        EffectiveRight = m.EffectiveRight,
        IsOver = m.IsOver,
        WinnerIdx = m.WinnerIndex
    };

    private static FistSnap CapFist(Fist f) => new()
    {
        State = (int)f.ChainState, X = f.Position.X, Y = f.Position.Y,
        Ax = f.AnchorPoint.X, Ay = f.AnchorPoint.Y, Cl = f.ChainLength,
        Dx = f.LaunchDirection.X, Dy = f.LaunchDirection.Y, Attached = f.IsAttachedToWorld
    };

    // ── Beacon pre-sim ──

    private void PreSimulateBeacon()
    {
        var rd = _beaconReplayData!;
        var arena = new BeaconArena(rd.Arena.Width, rd.Arena.Height, modifiers: rd.Arena.Modifiers);
        var firstCp = rd.Checkpoints[0];
        var rolesA = firstCp.P.Where(p => p.T == 0).Select(p => (PawnRole)p.R).ToArray();
        var rolesB = firstCp.P.Where(p => p.T == 1).Select(p => (PawnRole)p.R).ToArray();
        var match = new BeaconMatch(arena, rd.TeamATrees!.ToArray(), rolesA,
            rd.TeamBTrees!.ToArray(), rolesB, seed: rd.MatchSeed);

        // Capture rifle flashes per-tick via callback
        var pendingRifleFlashes = new List<RifleFlashSnap>();
        match.OnRifleFired = (segments, team) =>
        {
            pendingRifleFlashes.Add(new RifleFlashSnap { Segments = segments, Team = team });
        };

        while (!match.IsOver)
        {
            pendingRifleFlashes.Clear();
            match.Step();
            int justCompleted = match.Tick - 1;

            _timeline.Add(CaptureBeaconSnapshot(match, pendingRifleFlashes));

            if (_beaconCheckpoints.TryGetValue(justCompleted, out var expected))
            {
                var errors = new List<string>();
                if (match.Scores[0] != expected.S[0]) errors.Add($"Score[0]: expected {expected.S[0]}, got {match.Scores[0]}");
                if (match.Scores[1] != expected.S[1]) errors.Add($"Score[1]: expected {expected.S[1]}, got {match.Scores[1]}");
                for (int i = 0; i < match.AllPawns.Length && i < expected.P.Count; i++)
                {
                    var p = match.AllPawns[i]; var e = expected.P[i];
                    string n = $"Pawn[{p.TeamIndex}.{p.PawnIndex}]";
                    if (MathF.Abs(p.Position.X - e.X) > PosTolerance) errors.Add($"{n}.X: {e.X:F4} vs {p.Position.X:F4}");
                    if (MathF.Abs(p.Position.Y - e.Y) > PosTolerance) errors.Add($"{n}.Y: {e.Y:F4} vs {p.Position.Y:F4}");
                    if (MathF.Abs(p.Health - e.Hp) > HpTolerance) errors.Add($"{n}.Hp: {e.Hp:F4} vs {p.Health:F4}");
                    if (p.IsDead != e.D) errors.Add($"{n}.Dead: {e.D} vs {p.IsDead}");
                }
                RecordCheckResult(justCompleted, errors);
            }
        }
    }

    private TickSnapshot CaptureBeaconSnapshot(BeaconMatch m, List<RifleFlashSnap>? rifleFlashes = null)
    {
        var pawns = new PawnSnap[m.AllPawns.Length];
        for (int i = 0; i < m.AllPawns.Length; i++)
        {
            var p = m.AllPawns[i];
            pawns[i] = new PawnSnap
            {
                X = p.Position.X, Y = p.Position.Y, Vx = p.Velocity.X, Vy = p.Velocity.Y,
                Hp = p.Health, Grounded = p.IsGrounded, Dead = p.IsDead,
                Stunned = p.IsStunned, Vulnerable = p.IsVulnerable, ParryActive = p.IsParryActive,
                StunTicks = p.StunTicksRemaining, VulnTicks = p.VulnerableTicks,
                ParryCooldown = p.ParryCooldown, ParryActiveTicks = p.ParryActiveTicks,
                RespawnTimer = p.RespawnTimer,
                HookState = p.Role == PawnRole.Grappler ? (int)p.Hook.ChainState : 0,
                HookX = p.Role == PawnRole.Grappler ? p.Hook.Position.X : 0,
                HookY = p.Role == PawnRole.Grappler ? p.Hook.Position.Y : 0,
                HookAx = p.Role == PawnRole.Grappler ? p.Hook.AnchorPoint.X : 0,
                HookAy = p.Role == PawnRole.Grappler ? p.Hook.AnchorPoint.Y : 0,
                HookCl = p.Role == PawnRole.Grappler ? p.Hook.ChainLength : 0,
                HookAttached = p.Role == PawnRole.Grappler && p.Hook.IsAttachedToWorld
            };
        }
        var beacons = new BeaconSnap[m.Beacons.Length];
        for (int i = 0; i < m.Beacons.Length; i++)
            beacons[i] = new BeaconSnap { Owner = m.Beacons[i].OwnerTeam,
                CaptureProgress = m.Beacons[i].CaptureProgress, Contested = m.Beacons[i].IsContested };

        // Capture live projectiles
        var projSnaps = new ProjectileSnap[m.Projectiles.Count];
        for (int i = 0; i < m.Projectiles.Count; i++)
        {
            var proj = m.Projectiles[i];
            projSnaps[i] = new ProjectileSnap
            {
                X = proj.Position.X, Y = proj.Position.Y,
                Vx = proj.Velocity.X, Vy = proj.Velocity.Y,
                Gravity = proj.Gravity, Damage = proj.Damage, Knockback = proj.Knockback,
                Radius = proj.Radius, OwnerTeam = proj.OwnerTeam,
                OwnerPawnIndex = proj.OwnerPawnIndex,
                LifetimeRemaining = proj.LifetimeRemaining, IsAlive = proj.IsAlive
            };
        }

        return new TickSnapshot
        {
            Pawns = pawns, Beacons = beacons,
            Scores = [m.Scores[0], m.Scores[1]], Kills = [m.Kills[0], m.Kills[1]],
            Rates = [m.Rates[0], m.Rates[1]],
            Projectiles = projSnaps,
            RifleFlashes = rifleFlashes?.Count > 0 ? rifleFlashes.ToArray() : null,
            PickupActive = m.PickupActive.Length > 0 ? (bool[])m.PickupActive.Clone() : null,
            WallHp = m.DestructibleWallHp.Length > 0 ? (float[])m.DestructibleWallHp.Clone() : null,
            WallExists = m.DestructibleWallExists.Length > 0 ? (bool[])m.DestructibleWallExists.Clone() : null,
            EffectiveLeft = m.EffectiveLeft, EffectiveRight = m.EffectiveRight,
            IsOver = m.IsOver, WinnerIdx = m.WinnerTeam
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scene building (renderers bound to live mutable objects)
    // ═══════════════════════════════════════════════════════════════════════

    private void BuildFightScene()
    {
        var rd = _fightReplayData!;
        var arena = rd.Arena.Config != null ? new Arena(rd.Arena.Config) : new Arena(rd.Arena.Width, rd.Arena.Height);

        // Create a dummy match to hold mutable feature state for the renderer.
        // We use a noop BT — the match is never stepped, just used as a data container.
        var noopTree = new List<BtNode> { BtNode.Act("noop") };
        _replayMatch = new Match(arena, noopTree, noopTree);

        AddChild(new ArenaRenderer { Arena = arena, Match = _replayMatch });
        AddChild(new FighterRenderer { Fighter = _fighter0!, TeamColor = new Color(0.9f, 0.2f, 0.2f) });
        AddChild(new FighterRenderer { Fighter = _fighter1!, TeamColor = new Color(0.3f, 0.5f, 1f) });

        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);

        // Arena config label (top-left)
        var configLabel = new Label { Position = new Vector2(20, 10) };
        configLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f, 0.7f));
        configLabel.AddThemeFontSizeOverride("font_size", 12);
        var cfg = arena.Config;
        if (cfg.HasModifiers)
        {
            var parts = new List<string>();
            if (cfg.Platforms.Count > 0) parts.Add($"Platforms:{cfg.Platforms.Count}");
            if (cfg.HazardZones.Count > 0) parts.Add($"Hazards:{cfg.HazardZones.Count}");
            if (cfg.DestructibleWalls.Count > 0) parts.Add($"Walls:{cfg.DestructibleWalls.Count}");
            if (cfg.Pickups.Count > 0) parts.Add($"Pickups:{cfg.Pickups.Count}");
            if (cfg.Shrink != null) parts.Add("Shrink");
            if (cfg.Ceiling != null) parts.Add("Ceiling");
            if (cfg.CornerBumpers.Count > 0) parts.Add($"Bumpers:{cfg.CornerBumpers.Count}");
            configLabel.Text = $"Modifiers: {string.Join(" | ", parts)}";
        }
        else
            configLabel.Text = "Arena: Flat";
        canvasLayer.AddChild(configLabel);

        AddControlBar(canvasLayer);
        _hasMatch = true;
    }

    private void BuildBeaconScene()
    {
        var rd = _beaconReplayData!;
        var arena = new BeaconArena(rd.Arena.Width, rd.Arena.Height, modifiers: rd.Arena.Modifiers);

        // Create live pawn objects for renderers
        var firstCp = rd.Checkpoints[0];
        var rolesA = firstCp.P.Where(p => p.T == 0).Select(p => (PawnRole)p.R).ToArray();
        var rolesB = firstCp.P.Where(p => p.T == 1).Select(p => (PawnRole)p.R).ToArray();
        int teamSize = rd.TeamSize;
        _pawns = new Pawn[teamSize * 2];
        for (int i = 0; i < teamSize; i++)
        {
            _pawns[i] = new Pawn(0, i, rolesA[i], Vector2.Zero);
            _pawns[teamSize + i] = new Pawn(1, i, rolesB[i], Vector2.Zero);
        }
        _beacons = arena.BeaconZones.Select(z => new Beacon(z)).ToArray();
        _scores = [0, 0];
        _kills = [0, 0];

        // We need a BeaconMatch-like object for ScoreOverlay. Create a real one but never step it.
        _beaconMatchRef = new BeaconMatch(arena, rd.TeamATrees!.ToArray(), rolesA,
            rd.TeamBTrees!.ToArray(), rolesB);

        var colorA = new Color(0.16f, 0.5f, 0.73f);
        var colorB = new Color(0.56f, 0.27f, 0.68f);

        AddChild(new BeaconArenaRenderer
        {
            Arena = arena, Beacons = _beacons, Match = _beaconMatchRef,
            TeamAColor = colorA, TeamBColor = colorB
        });
        foreach (var pawn in _pawns)
        {
            AddChild(new PawnRenderer
            {
                Pawn = pawn, TeamColor = pawn.TeamIndex == 0 ? colorA : colorB
            });
        }
        _projectileRenderer = new ProjectileRenderer
        {
            Match = _beaconMatchRef, TeamAColor = colorA, TeamBColor = colorB
        };
        AddChild(_projectileRenderer);

        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);
        canvasLayer.AddChild(new ScoreOverlay
        {
            Match = _beaconMatchRef, TeamAColor = colorA, TeamBColor = colorB,
            TeamAName = "Team A", TeamBName = "Team B"
        });

        AddControlBar(canvasLayer);
        _hasMatch = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Snapshot application — write pre-computed state to live objects
    // ═══════════════════════════════════════════════════════════════════════

    private void ApplySnapshot(int tick)
    {
        if (tick < 0 || tick >= _timeline.Count) return;
        var snap = _timeline[tick];

        if (!_isBeaconBrawl && snap.Fighters != null && snap.Fists != null)
        {
            ApplyFighterSnap(_fighter0!, snap.Fighters[0], snap.Fists[0], snap.Fists[1]);
            ApplyFighterSnap(_fighter1!, snap.Fighters[1], snap.Fists[2], snap.Fists[3]);

            // Restore feature state so ArenaRenderer draws walls/pickups/shrink correctly
            if (_replayMatch != null)
            {
                if (snap.WallHp != null && _replayMatch.DestructibleWallHp.Length > 0)
                    Array.Copy(snap.WallHp, _replayMatch.DestructibleWallHp,
                        Math.Min(snap.WallHp.Length, _replayMatch.DestructibleWallHp.Length));
                if (snap.WallExists != null && _replayMatch.DestructibleWallExists.Length > 0)
                    Array.Copy(snap.WallExists, _replayMatch.DestructibleWallExists,
                        Math.Min(snap.WallExists.Length, _replayMatch.DestructibleWallExists.Length));
                if (snap.PickupActive != null && _replayMatch.PickupActive.Length > 0)
                    Array.Copy(snap.PickupActive, _replayMatch.PickupActive,
                        Math.Min(snap.PickupActive.Length, _replayMatch.PickupActive.Length));
                _replayMatch.EffectiveLeft = snap.EffectiveLeft;
                _replayMatch.EffectiveRight = snap.EffectiveRight;
            }
        }
        else if (_isBeaconBrawl && snap.Pawns != null && _pawns != null)
        {
            for (int i = 0; i < _pawns.Length && i < snap.Pawns.Length; i++)
                ApplyPawnSnap(_pawns[i], snap.Pawns[i]);
            if (snap.Beacons != null && _beacons != null)
                for (int i = 0; i < _beacons.Length && i < snap.Beacons.Length; i++)
                    ApplyBeaconSnap(_beacons[i], snap.Beacons[i]);
            // Update score overlay's match reference
            if (_beaconMatchRef != null && snap.Scores != null)
            {
                _beaconMatchRef.Scores[0] = snap.Scores[0];
                _beaconMatchRef.Scores[1] = snap.Scores[1];
                _beaconMatchRef.Kills[0] = snap.Kills?[0] ?? 0;
                _beaconMatchRef.Kills[1] = snap.Kills?[1] ?? 0;
                _beaconMatchRef.Rates[0] = snap.Rates?[0] ?? 0;
                _beaconMatchRef.Rates[1] = snap.Rates?[1] ?? 0;
                _beaconMatchRef.Tick = tick;
                _beaconMatchRef.IsOver = snap.IsOver;
                _beaconMatchRef.WinnerTeam = snap.WinnerIdx;

                // Sync pawn state to match ref so ScoreOverlay can read health
                for (int i = 0; i < _beaconMatchRef.TeamA.Length && i < snap.Pawns!.Length; i++)
                    ApplyPawnSnap(_beaconMatchRef.TeamA[i], snap.Pawns[i]);
                for (int i = 0; i < _beaconMatchRef.TeamB.Length; i++)
                {
                    int si = _beaconMatchRef.TeamA.Length + i;
                    if (si < snap.Pawns!.Length)
                        ApplyPawnSnap(_beaconMatchRef.TeamB[i], snap.Pawns[si]);
                }

                // Sync beacon state to match ref
                if (snap.Beacons != null)
                    for (int i = 0; i < _beaconMatchRef.Beacons.Length && i < snap.Beacons.Length; i++)
                        ApplyBeaconSnap(_beaconMatchRef.Beacons[i], snap.Beacons[i]);

                // Restore projectiles so ProjectileRenderer can draw them
                _beaconMatchRef.Projectiles.Clear();
                if (snap.Projectiles != null)
                {
                    foreach (var ps in snap.Projectiles)
                    {
                        if (!ps.IsAlive) continue;
                        var proj = new Projectile(
                            new Vector2(ps.X, ps.Y), new Vector2(ps.Vx, ps.Vy),
                            ps.Gravity, ps.Damage, ps.Knockback,
                            ps.OwnerTeam, ps.OwnerPawnIndex, ps.LifetimeRemaining);
                        proj.Radius = ps.Radius;
                        _beaconMatchRef.Projectiles.Add(proj);
                    }
                }

                // Restore modifier feature state (pickups, walls, shrink)
                if (snap.PickupActive != null && _beaconMatchRef.PickupActive.Length > 0)
                    Array.Copy(snap.PickupActive, _beaconMatchRef.PickupActive,
                        Math.Min(snap.PickupActive.Length, _beaconMatchRef.PickupActive.Length));
                if (snap.WallHp != null && _beaconMatchRef.DestructibleWallHp.Length > 0)
                    Array.Copy(snap.WallHp, _beaconMatchRef.DestructibleWallHp,
                        Math.Min(snap.WallHp.Length, _beaconMatchRef.DestructibleWallHp.Length));
                if (snap.WallExists != null && _beaconMatchRef.DestructibleWallExists.Length > 0)
                    Array.Copy(snap.WallExists, _beaconMatchRef.DestructibleWallExists,
                        Math.Min(snap.WallExists.Length, _beaconMatchRef.DestructibleWallExists.Length));
                _beaconMatchRef.EffectiveLeft = snap.EffectiveLeft;
                _beaconMatchRef.EffectiveRight = snap.EffectiveRight;

                // Feed rifle flashes to renderer
                if (snap.RifleFlashes != null && _projectileRenderer != null)
                {
                    foreach (var rf in snap.RifleFlashes)
                        _projectileRenderer.NotifyRifleShot(rf.Segments, rf.Team);
                }
            }
        }
    }

    private static void ApplyFighterSnap(Fighter f, FighterSnap s, FistSnap left, FistSnap right)
    {
        f.Position = new Vector2(s.X, s.Y);
        f.Velocity = new Vector2(s.Vx, s.Vy);
        f.Health = s.Hp;
        f.IsGrounded = s.Grounded;
        ApplyFistSnap(f.LeftFist, left);
        ApplyFistSnap(f.RightFist, right);
    }

    private static void ApplyFistSnap(Fist fist, FistSnap s)
    {
        fist.ForceState((FistChainState)s.State, new Vector2(s.X, s.Y),
            new Vector2(s.Ax, s.Ay), s.Cl, s.Attached, new Vector2(s.Dx, s.Dy));
    }

    private static void ApplyPawnSnap(Pawn p, PawnSnap s)
    {
        p.Position = new Vector2(s.X, s.Y);
        p.Velocity = new Vector2(s.Vx, s.Vy);
        p.Health = s.Hp;
        p.IsGrounded = s.Grounded;
        p.IsDead = s.Dead;
        p.IsStunned = s.Stunned;
        p.StunTicksRemaining = s.StunTicks;
        p.VulnerableTicks = s.VulnTicks;
        p.ParryCooldown = s.ParryCooldown;
        p.ParryActiveTicks = s.ParryActiveTicks;
        p.RespawnTimer = s.RespawnTimer;
        if (p.Role == PawnRole.Grappler)
        {
            p.Hook.ForceState((FistChainState)s.HookState, new Vector2(s.HookX, s.HookY),
                new Vector2(s.HookAx, s.HookAy), s.HookCl, s.HookAttached);
        }
    }

    private static void ApplyBeaconSnap(Beacon b, BeaconSnap s)
    {
        b.ForceState(s.Owner, s.CaptureProgress, s.Contested);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Playback loop
    // ═══════════════════════════════════════════════════════════════════════

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasMatch || _timeline.Count == 0) return;

        if (!_paused && _displayTick < _finalTick)
        {
            _displayTick++;
            ApplySnapshot(_displayTick);
        }

        UpdateHud();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HUD
    // ═══════════════════════════════════════════════════════════════════════

    private void AddControlBar(CanvasLayer layer)
    {
        var bar = new PanelContainer();
        bar.AnchorLeft = 0; bar.AnchorRight = 1;
        bar.AnchorTop = 1; bar.AnchorBottom = 1;
        bar.OffsetTop = -48; bar.OffsetBottom = 0;
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.09f, 0.9f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 6, ContentMarginBottom = 6
        });
        layer.AddChild(bar);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 12);
        bar.AddChild(hbox);

        _playPauseBtn = new Button { Text = "  ⏸  ", CustomMinimumSize = new Vector2(50, 0) };
        _playPauseBtn.AddThemeFontSizeOverride("font_size", 16);
        _playPauseBtn.Pressed += () => { _paused = !_paused; UpdatePlayPauseBtn(); };
        hbox.AddChild(_playPauseBtn);

        _tickLabel = new Label { Text = "0 / 0", CustomMinimumSize = new Vector2(140, 0) };
        _tickLabel.AddThemeColorOverride("font_color", Colors.White);
        _tickLabel.AddThemeFontSizeOverride("font_size", 13);
        _tickLabel.VerticalAlignment = VerticalAlignment.Center;
        hbox.AddChild(_tickLabel);

        _scrubBar = new HSlider
        {
            MinValue = 0, MaxValue = Math.Max(1, _finalTick), Value = 0, Step = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(200, 0)
        };
        _scrubBar.DragStarted += () => { _scrubbing = true; };
        _scrubBar.DragEnded += (_) => { _scrubbing = false; };
        _scrubBar.ValueChanged += (val) =>
        {
            if (_scrubbing)
            {
                _displayTick = (int)val;
                ApplySnapshot(_displayTick);
                UpdateHud();
            }
        };
        hbox.AddChild(_scrubBar);

        _divergenceLabel = new Label
        {
            Text = _diverged ? $"DIVERGED tick {_firstDivergenceTick}" : $"SYNC OK  |  {_checkpointsVerified} checkpoints",
            CustomMinimumSize = new Vector2(280, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _divergenceLabel.AddThemeColorOverride("font_color", _diverged ? Colors.Red : Colors.LimeGreen);
        _divergenceLabel.AddThemeFontSizeOverride("font_size", 13);
        hbox.AddChild(_divergenceLabel);
    }

    private void UpdatePlayPauseBtn()
    {
        if (_playPauseBtn != null)
            _playPauseBtn.Text = _paused ? "  ▶  " : "  ⏸  ";
    }

    private void UpdateHud()
    {
        if (_tickLabel != null)
            _tickLabel.Text = $"Tick {_displayTick} / {_finalTick}  ({_displayTick / 60f:F1}s)";
        if (_scrubBar != null && !_scrubbing)
            _scrubBar.SetValueNoSignal(_displayTick);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Camera
    // ═══════════════════════════════════════════════════════════════════════

    private void FitCameraToArena()
    {
        if (_camera == null) return;
        float arenaW = 1500, arenaH = 680;
        if (_isBeaconBrawl && _beaconReplayData != null)
        { arenaW = _beaconReplayData.Arena.Width; arenaH = _beaconReplayData.Arena.Height; }
        else if (_fightReplayData != null)
        { arenaW = _fightReplayData.Arena.Width; arenaH = _fightReplayData.Arena.Height; }

        _camera.Position = new Vector2(arenaW / 2f, arenaH / 2f);
        var vp = GetViewportRect().Size;
        if (vp.X <= 0 || vp.Y <= 0) return;
        float zoom = Mathf.Min(vp.X / (arenaW + 40f), vp.Y / (arenaH + 40f));
        _camera.Zoom = new Vector2(Mathf.Clamp(zoom, ZoomMin, ZoomMax),
                                    Mathf.Clamp(zoom, ZoomMin, ZoomMax));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Divergence helpers (used during pre-sim only)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckFighter(List<string> errors, int idx, Fighter actual, ReplayFighter expected)
    {
        string p = $"F{idx}";
        if (MathF.Abs(actual.Position.X - expected.X) > PosTolerance) errors.Add($"{p}.X: {expected.X:F2} vs {actual.Position.X:F2}");
        if (MathF.Abs(actual.Position.Y - expected.Y) > PosTolerance) errors.Add($"{p}.Y: {expected.Y:F2} vs {actual.Position.Y:F2}");
        if (MathF.Abs(actual.Velocity.X - expected.Vx) > VelTolerance) errors.Add($"{p}.Vx: {expected.Vx:F2} vs {actual.Velocity.X:F2}");
        if (MathF.Abs(actual.Velocity.Y - expected.Vy) > VelTolerance) errors.Add($"{p}.Vy: {expected.Vy:F2} vs {actual.Velocity.Y:F2}");
        if (MathF.Abs(actual.Health - expected.Hp) > HpTolerance) errors.Add($"{p}.Hp: {expected.Hp:F2} vs {actual.Health:F2}");
        if (actual.IsGrounded != expected.G) errors.Add($"{p}.Gnd: {expected.G} vs {actual.IsGrounded}");
    }

    private void CheckFist(List<string> errors, string name, Fist actual, ReplayFist expected)
    {
        if ((int)actual.ChainState != expected.S) errors.Add($"{name}.St: {expected.S} vs {(int)actual.ChainState}");
        if (MathF.Abs(actual.Position.X - expected.X) > PosTolerance) errors.Add($"{name}.X: {expected.X:F2} vs {actual.Position.X:F2}");
        if (MathF.Abs(actual.Position.Y - expected.Y) > PosTolerance) errors.Add($"{name}.Y: {expected.Y:F2} vs {actual.Position.Y:F2}");
        if (MathF.Abs(actual.ChainLength - expected.Cl) > ChainTolerance) errors.Add($"{name}.Cl: {expected.Cl:F2} vs {actual.ChainLength:F2}");
        if (actual.IsAttachedToWorld != expected.A) errors.Add($"{name}.At: {expected.A} vs {actual.IsAttachedToWorld}");
    }

    private void RecordCheckResult(int tick, List<string> errors)
    {
        if (errors.Count > 0)
        {
            _checkpointsFailed++;
            if (!_diverged) { _diverged = true; _firstDivergenceTick = tick; GD.PrintErr($"!!! DIVERGENCE at tick {tick} !!!"); }
            foreach (var err in errors) { GD.PrintErr($"  [{tick}] {err}"); _divergenceLog.Add($"[{tick}] {err}"); }
        }
        else _checkpointsVerified++;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Error display
    // ═══════════════════════════════════════════════════════════════════════

    private void ShowError(string message)
    {
        GD.PrintErr(message);
        var cl = new CanvasLayer(); AddChild(cl);
        var bg = new ColorRect { Color = new Color(0.05f, 0.07f, 0.09f), AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        cl.AddChild(bg);
        var lbl = new Label { Text = message, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, AnchorsPreset = (int)Control.LayoutPreset.FullRect };
        lbl.AddThemeColorOverride("font_color", new Color(0.97f, 0.32f, 0.29f));
        lbl.AddThemeFontSizeOverride("font_size", 20);
        cl.AddChild(lbl);
        var hint = new Label { Text = "Press Escape to close", Position = new Vector2(0, 600),
            HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(2000, 40) };
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.62f));
        hint.AddThemeFontSizeOverride("font_size", 14);
        cl.AddChild(hint);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Input
    // ═══════════════════════════════════════════════════════════════════════

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && _camera != null)
        {
            switch (mb.ButtonIndex)
            {
                case MouseButton.Middle:
                    _dragging = mb.Pressed;
                    break;
                case MouseButton.WheelUp when mb.Pressed:
                    var zu = _camera.Zoom;
                    _camera.Zoom = new Vector2(Mathf.Clamp(zu.X + ZoomStep, ZoomMin, ZoomMax),
                                                Mathf.Clamp(zu.Y + ZoomStep, ZoomMin, ZoomMax));
                    break;
                case MouseButton.WheelDown when mb.Pressed:
                    var zd = _camera.Zoom;
                    _camera.Zoom = new Vector2(Mathf.Clamp(zd.X - ZoomStep, ZoomMin, ZoomMax),
                                                Mathf.Clamp(zd.Y - ZoomStep, ZoomMin, ZoomMax));
                    break;
            }
        }

        if (@event is InputEventMouseMotion mm && _dragging && _camera != null)
            _camera.Position -= mm.Relative / _camera.Zoom;

        if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Space when _hasMatch:
                    _paused = !_paused; UpdatePlayPauseBtn(); break;
                case Key.Right when _hasMatch:
                    _paused = true; UpdatePlayPauseBtn();
                    if (_displayTick < _finalTick) { _displayTick++; ApplySnapshot(_displayTick); }
                    break;
                case Key.Left when _hasMatch:
                    _paused = true; UpdatePlayPauseBtn();
                    if (_displayTick > 0) { _displayTick--; ApplySnapshot(_displayTick); }
                    break;
                case Key.Home when _camera != null:
                    FitCameraToArena(); break;
                case Key.R when _hasMatch:
                    _displayTick = 0; ApplySnapshot(0); break;
                case Key.Escape:
                    GetTree().Quit(); break;
                case Key.F12 when _hasMatch:
                    TakeScreenshot(); break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Screenshot (F12) — saves PNG + JSON with game state
    // ═══════════════════════════════════════════════════════════════════════

    private void TakeScreenshot()
    {
        var dir = ProjectSettings.GlobalizePath("res://debug/screenshots");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        string pngPath = Path.Combine(dir, $"{ts}.png");
        string jsonPath = Path.Combine(dir, $"{ts}.json");

        // Capture viewport image
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(pngPath);

        // Build state JSON
        var state = BuildScreenshotState(ts);
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        File.WriteAllText(jsonPath, json);

        GD.Print($"Screenshot saved: {pngPath}");
    }

    private object BuildScreenshotState(string timestamp)
    {
        var snap = _displayTick >= 0 && _displayTick < _timeline.Count ? _timeline[_displayTick] : default;

        // Run config — pull modifier info from live match objects
        object? runConfig = null;
        if (_isBeaconBrawl && _beaconReplayData != null)
        {
            var rd = _beaconReplayData;
            var mods = _beaconMatchRef?.Arena.Modifiers;
            var modList = new List<string>();
            if (mods != null && mods.HasModifiers)
            {
                if (mods.Platforms.Count > 0) modList.Add($"platforms:{mods.Platforms.Count}");
                if (mods.HazardZones.Count > 0) modList.Add($"hazards:{mods.HazardZones.Count}");
                if (mods.DestructibleWalls.Count > 0) modList.Add($"walls:{mods.DestructibleWalls.Count}");
                if (mods.Pickups.Count > 0) modList.Add($"pickups:{mods.Pickups.Count}");
                if (mods.Shrink != null) modList.Add("shrink");
                if (mods.Ceiling != null) modList.Add("ceiling");
                if (mods.CornerBumpers.Count > 0) modList.Add($"bumpers:{mods.CornerBumpers.Count}");
                if (mods.WallFrictionZones.Count > 0) modList.Add($"friction:{mods.WallFrictionZones.Count}");
            }
            runConfig = new
            {
                mode = "beacon_brawl",
                arena_width = rd.Arena.Width,
                arena_height = rd.Arena.Height,
                team_size = rd.TeamSize,
                match_seed = rd.MatchSeed,
                beacon_count = rd.Arena.BeaconCenters?.Length ?? 3,
                map = modList.Count > 0 ? string.Join(", ", modList) : "flat (no modifiers)"
            };
        }
        else if (_fightReplayData != null)
        {
            var rd = _fightReplayData;
            var cfg = rd.Arena.Config;
            var modList = new List<string>();
            if (cfg != null && cfg.HasModifiers)
            {
                if (cfg.Platforms.Count > 0) modList.Add($"platforms:{cfg.Platforms.Count}");
                if (cfg.HazardZones.Count > 0) modList.Add($"hazards:{cfg.HazardZones.Count}");
                if (cfg.DestructibleWalls.Count > 0) modList.Add($"walls:{cfg.DestructibleWalls.Count}");
                if (cfg.Pickups.Count > 0) modList.Add($"pickups:{cfg.Pickups.Count}");
                if (cfg.Shrink != null) modList.Add("shrink");
                if (cfg.Ceiling != null) modList.Add("ceiling");
                if (cfg.CornerBumpers.Count > 0) modList.Add($"bumpers:{cfg.CornerBumpers.Count}");
                if (cfg.WallFrictionZones.Count > 0) modList.Add($"friction:{cfg.WallFrictionZones.Count}");
            }
            runConfig = new
            {
                mode = "fight",
                arena_width = rd.Arena.Width,
                arena_height = rd.Arena.Height,
                match_seed = rd.MatchSeed,
                map = modList.Count > 0 ? string.Join(", ", modList) : "flat (no modifiers)"
            };
        }

        // Current positions
        object? positions = null;
        if (!_isBeaconBrawl && snap.Fighters != null)
        {
            positions = new
            {
                fighter0 = new { x = snap.Fighters[0].X, y = snap.Fighters[0].Y, hp = snap.Fighters[0].Hp, grounded = snap.Fighters[0].Grounded },
                fighter1 = new { x = snap.Fighters[1].X, y = snap.Fighters[1].Y, hp = snap.Fighters[1].Hp, grounded = snap.Fighters[1].Grounded }
            };
        }
        else if (_isBeaconBrawl && snap.Pawns != null)
        {
            var pawnStates = new object[snap.Pawns.Length];
            for (int i = 0; i < snap.Pawns.Length; i++)
            {
                var p = snap.Pawns[i];
                pawnStates[i] = new
                {
                    index = i, team = i < snap.Pawns.Length / 2 ? 0 : 1,
                    x = p.X, y = p.Y, hp = p.Hp, dead = p.Dead, stunned = p.Stunned,
                    vulnerable = p.Vulnerable, parry_active = p.ParryActive
                };
            }
            positions = new { pawns = pawnStates, scores = snap.Scores, kills = snap.Kills };
        }

        // Feature state
        object? featureState = null;
        if (!_isBeaconBrawl && snap.WallHp != null)
        {
            featureState = new
            {
                wall_hp = snap.WallHp, wall_exists = snap.WallExists,
                pickup_active = snap.PickupActive,
                effective_left = snap.EffectiveLeft, effective_right = snap.EffectiveRight
            };
        }
        else if (_isBeaconBrawl && snap.Beacons != null)
        {
            var beaconStates = new object[snap.Beacons.Length];
            for (int i = 0; i < snap.Beacons.Length; i++)
                beaconStates[i] = new { owner = snap.Beacons[i].Owner, capture_progress = snap.Beacons[i].CaptureProgress, contested = snap.Beacons[i].Contested };
            featureState = new { beacons = beaconStates, rates = snap.Rates };
        }

        // Build timestamp from DLL modification time
        string buildTime = "unknown";
        var asm = System.Reflection.Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(asm) && File.Exists(asm))
            buildTime = File.GetLastWriteTime(asm).ToString("yyyy-MM-dd HH:mm:ss");
        else
        {
            // Fallback: search for project DLL in .godot build output
            var dllPath = Path.Combine(ProjectSettings.GlobalizePath("res://"), ".godot", "mono", "temp", "bin", "Debug", "AI-BT-Gym.dll");
            if (File.Exists(dllPath))
                buildTime = File.GetLastWriteTime(dllPath).ToString("yyyy-MM-dd HH:mm:ss");
        }

        return new
        {
            timestamp,
            build = buildTime,
            match_name = _matchName,
            replay_file = _battleLogPath,
            tick = _displayTick,
            total_ticks = _finalTick,
            time_seconds = _displayTick / 60f,
            is_beacon_brawl = _isBeaconBrawl,
            run_config = runConfig,
            positions,
            feature_state = featureState,
            is_over = snap.IsOver,
            winner = snap.WinnerIdx
        };
    }
}

internal record ReplayConfig
{
    public string? BattleLogPath { get; init; }
}
