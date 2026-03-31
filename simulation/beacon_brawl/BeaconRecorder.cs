// ─────────────────────────────────────────────────────────────────────────────
// BeaconRecorder.cs — Captures match events for Beacon Brawl v2 battle logs
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation.BeaconBrawl;

public class BeaconRecorder
{
    private readonly string[] _teamNames;
    private readonly string?[] _teamColors;
    private readonly int _generation;
    private readonly string _matchId;

    // Per-team, per-pawn action frequency
    private readonly Dictionary<string, int>[,] _actionCounts;

    // Per-tick samples
    private readonly List<TickSample> _samples = [];

    // Beacon ownership tracking per tick
    private readonly List<int[]> _beaconOwnerHistory = [];

    // Combat tracking
    private readonly int[] _kills = [0, 0];
    private readonly int[] _deaths = [0, 0];
    private readonly float[] _damageDealt = [0, 0];
    private readonly float[] _damageReceived = [0, 0];
    private readonly int[] _fistHits = [0, 0];
    private readonly int[] _pistolHits = [0, 0];
    private readonly int[] _rifleHits = [0, 0];
    private readonly int[] _hookGrabs = [0, 0];
    private readonly int[] _parrySuccesses = [0, 0];

    // Events log
    private readonly List<(int tick, int team, int pawnIdx, string eventType)> _events = [];

    // Replay-specific event lists
    private readonly List<ReplayPistolShot> _replayPistolShots = [];
    private readonly List<ReplayRifleShot> _replayRifleShots = [];
    private readonly List<ReplayHitEvent> _replayHitEvents = [];

    // Capture events
    private readonly List<(int tick, int beaconIdx, int newOwner)> _captureEvents = [];

    // Replay checkpoints
    private const int CheckpointInterval = 10;
    private readonly List<BeaconReplayCheckpoint> _checkpoints = [];

    private int _teamSize;

    public BeaconRecorder(string[] teamNames, int generation, string matchId,
        string?[]? teamColors = null, int teamSize = 2)
    {
        _teamNames = teamNames;
        _teamColors = teamColors ?? [null, null];
        _generation = generation;
        _matchId = matchId;
        _teamSize = teamSize;

        _actionCounts = new Dictionary<string, int>[2, teamSize];
        for (int t = 0; t < 2; t++)
            for (int p = 0; p < teamSize; p++)
                _actionCounts[t, p] = new();
    }

    public void RecordAction(int tick, int teamIdx, int pawnIdx, string action)
    {
        var counts = _actionCounts[teamIdx, pawnIdx];
        counts.TryGetValue(action, out int count);
        counts[action] = count + 1;
    }

    public void RecordEvent(int tick, int teamIdx, int pawnIdx, string eventType)
    {
        _events.Add((tick, teamIdx, pawnIdx, eventType));

        if (eventType == "death") _deaths[teamIdx]++;
        if (eventType == "kill") _kills[teamIdx]++;
        if (eventType.StartsWith("hook_grab")) _hookGrabs[teamIdx]++;
        if (eventType.StartsWith("fist_hit")) _fistHits[teamIdx]++;
        if (eventType.StartsWith("pistol_hit")) _pistolHits[teamIdx]++;
        if (eventType.StartsWith("rifle_hit")) _rifleHits[teamIdx]++;
        if (eventType.StartsWith("parry_success")) _parrySuccesses[teamIdx]++;

        // Record hit events for replay viewer (flash on hit)
        string? prefix = null;
        if (eventType.StartsWith("fist_hit_")) prefix = "fist_hit_";
        else if (eventType.StartsWith("pistol_hit_")) prefix = "pistol_hit_";
        else if (eventType.StartsWith("rifle_hit_")) prefix = "rifle_hit_";
        if (prefix != null)
        {
            var parts = eventType.Substring(prefix.Length).Split('_');
            if (parts.Length == 2
                && int.TryParse(parts[0], out int tTeam)
                && int.TryParse(parts[1], out int tPawn))
            {
                _replayHitEvents.Add(new ReplayHitEvent
                {
                    Tk = tick,
                    Pi = tTeam * _teamSize + tPawn
                });
            }
        }
    }

    public void RecordDamage(int attackerTeam, float amount)
    {
        _damageDealt[attackerTeam] += amount;
        _damageReceived[1 - attackerTeam] += amount;
    }

    public void RecordPistolShot(int tick, int teamIdx, int pawnIdx, Vector2 position, Vector2 velocity)
    {
        int shooterAllIdx = teamIdx * _teamSize + pawnIdx;
        _replayPistolShots.Add(new ReplayPistolShot
        {
            Tk = tick, Pi = shooterAllIdx,
            X = position.X, Y = position.Y,
            Vx = velocity.X, Vy = velocity.Y,
            T = teamIdx
        });
    }

    public void RecordRifleShot(int tick, int teamIdx, int pawnIdx, List<Vector2> segments, Pawn? hitPawn)
    {
        bool hit = hitPawn != null;
        if (hit) _rifleHits[teamIdx]++;

        int shooterAllIdx = teamIdx * _teamSize + pawnIdx;
        int hitPawnAllIdx = hitPawn != null ? hitPawn.TeamIndex * _teamSize + hitPawn.PawnIndex : -1;

        _replayRifleShots.Add(new ReplayRifleShot
        {
            Tk = tick,
            Pi = shooterAllIdx,
            Sg = segments.Select(v => new[] { v.X, v.Y }).ToArray(),
            H = hit,
            Hp = hitPawnAllIdx
        });

        if (hit && hitPawnAllIdx >= 0)
        {
            _replayHitEvents.Add(new ReplayHitEvent { Tk = tick, Pi = hitPawnAllIdx });
        }
    }

    public void RecordTick(BeaconMatch match)
    {
        // Beacon ownership snapshot
        _beaconOwnerHistory.Add([
            match.Beacons[0].OwnerTeam,
            match.Beacons[1].OwnerTeam,
            match.Beacons[2].OwnerTeam
        ]);

        // Track capture events
        if (_beaconOwnerHistory.Count >= 2)
        {
            var prev = _beaconOwnerHistory[^2];
            var curr = _beaconOwnerHistory[^1];
            for (int i = 0; i < 3; i++)
            {
                if (curr[i] != prev[i] && curr[i] != 0)
                    _captureEvents.Add((match.Tick, i, curr[i]));
            }
        }

        // Positional sample
        var sample = new TickSample
        {
            Tick = match.Tick,
            Scores = [match.Scores[0], match.Scores[1]],
            PawnPositions = new Vector2[match.AllPawns.Length],
            PawnGrounded = new bool[match.AllPawns.Length],
            PawnDead = new bool[match.AllPawns.Length],
            PawnTeams = new int[match.AllPawns.Length],
            PawnHealth = new float[match.AllPawns.Length],
        };

        for (int i = 0; i < match.AllPawns.Length; i++)
        {
            var p = match.AllPawns[i];
            sample.PawnPositions[i] = p.Position;
            sample.PawnGrounded[i] = p.IsGrounded;
            sample.PawnDead[i] = p.IsDead;
            sample.PawnTeams[i] = p.TeamIndex;
            sample.PawnHealth[i] = p.Health;
        }

        _samples.Add(sample);

        // Replay checkpoint
        if (match.Tick % CheckpointInterval == 0)
            CaptureCheckpoint(match);
    }

    public BeaconBattleLog BuildBattleLog(int perspectiveTeam)
    {
        int oppTeam = 1 - perspectiveTeam;
        int myTeamId = perspectiveTeam + 1;
        int totalTicks = _samples.Count;
        if (totalTicks == 0)
            return new BeaconBattleLog { MatchId = _matchId };

        var lastSample = _samples[^1];

        // Result
        MatchResult result;
        int myScore = lastSample.Scores[perspectiveTeam];
        int oppScore = lastSample.Scores[oppTeam];
        if (myScore > oppScore) result = MatchResult.Win;
        else if (myScore < oppScore) result = MatchResult.Loss;
        else result = MatchResult.Draw;

        // Beacon control
        int owningLeftTicks = 0, owningCenterTicks = 0, owningRightTicks = 0;
        foreach (var snap in _beaconOwnerHistory)
        {
            if (snap[0] == myTeamId) owningLeftTicks++;
            if (snap[1] == myTeamId) owningCenterTicks++;
            if (snap[2] == myTeamId) owningRightTicks++;
        }

        int myCaps = _captureEvents.Count(e => e.newOwner == myTeamId);
        int myLost = _captureEvents.Count(e =>
        {
            int idx = _captureEvents.IndexOf(e);
            if (idx > 0)
            {
                var prevSnap = _beaconOwnerHistory[Math.Max(0, e.tick - 1)];
                return prevSnap[e.beaconIdx] == myTeamId && e.newOwner != myTeamId;
            }
            return false;
        });

        // Positional summary
        float avgX = 0, avgY = 0;
        int groundedTicks = 0, platformTicks = 0, inBeaconTicks = 0, inBaseTicks = 0;
        float totalDistToEnemy = 0;
        int pawnSamples = 0;

        for (int s = 0; s < _samples.Count; s++)
        {
            var sample = _samples[s];
            for (int i = 0; i < sample.PawnPositions.Length; i++)
            {
                if (sample.PawnTeams[i] != perspectiveTeam) continue;
                if (sample.PawnDead[i]) continue;
                var pos = sample.PawnPositions[i];
                avgX += pos.X;
                avgY += pos.Y;
                if (sample.PawnGrounded[i]) groundedTicks++;

                // Platform check (approximate — near platform top, within horizontal bounds)
                if (pos.Y < 700 && pos.Y > 600 && pos.X > 800 && pos.X < 1200)
                    platformTicks++;

                // Beacon zone check (any of the 3 beacons)
                foreach (var bz in _beaconOwnerHistory.Count > 0 ? Array.Empty<int>() : Array.Empty<int>())
                    ; // skip — use position-based check instead
                // Simple distance-based beacon check (radius 80)
                bool inAnyBeacon = false;
                float[] beaconXs = [500f, 1000f, 1500f];
                float[] beaconYs = [774f, 610f, 774f]; // ground level / platform beacon Y / ground level
                for (int b = 0; b < 3; b++)
                {
                    float dx = pos.X - beaconXs[b];
                    float dy = pos.Y - beaconYs[b];
                    if (dx * dx + dy * dy < 80f * 80f) { inAnyBeacon = true; break; }
                }
                if (inAnyBeacon) inBeaconTicks++;

                // Base zone check (team spawn corners)
                float baseX = perspectiveTeam == 0 ? 100f : 1900f;
                float baseDist = pos.DistanceTo(new Vector2(baseX, 774f));
                if (baseDist < 100f) inBaseTicks++;

                // Distance to nearest enemy
                float nearestEDist = float.MaxValue;
                for (int j = 0; j < sample.PawnPositions.Length; j++)
                {
                    if (sample.PawnTeams[j] == perspectiveTeam || sample.PawnDead[j]) continue;
                    float d = pos.DistanceTo(sample.PawnPositions[j]);
                    if (d < nearestEDist) nearestEDist = d;
                }
                if (nearestEDist < float.MaxValue) totalDistToEnemy += nearestEDist;

                pawnSamples++;
            }
        }

        if (pawnSamples > 0) { avgX /= pawnSamples; avgY /= pawnSamples; }

        // Aggregate action frequency
        var aggActions = new Dictionary<string, int>();
        for (int p = 0; p < _teamSize; p++)
        {
            foreach (var (action, count) in _actionCounts[perspectiveTeam, p])
            {
                aggActions.TryGetValue(action, out int existing);
                aggActions[action] = existing + count;
            }
        }

        // Key moments
        var keyMoments = BuildKeyMoments(perspectiveTeam);

        return new BeaconBattleLog
        {
            MatchId = _matchId,
            Generation = _generation,
            Team = _teamNames[perspectiveTeam],
            TeamColor = _teamColors[perspectiveTeam],
            Opponent = _teamNames[oppTeam],
            OpponentColor = _teamColors[oppTeam],
            Result = result,
            DurationTicks = totalTicks,
            DurationSeconds = totalTicks * SimPhysics.FixedDt,
            FinalScores = [myScore, oppScore],
            BeaconControl = new BeaconControlSummary
            {
                TimeOwningLeftPct = totalTicks > 0 ? (float)owningLeftTicks / totalTicks : 0,
                TimeOwningCenterPct = totalTicks > 0 ? (float)owningCenterTicks / totalTicks : 0,
                TimeOwningRightPct = totalTicks > 0 ? (float)owningRightTicks / totalTicks : 0,
                TotalBeaconOwnershipPct = totalTicks > 0 ? (float)(owningLeftTicks + owningCenterTicks + owningRightTicks) / (totalTicks * 3) : 0,
                CaptureCount = myCaps,
                LostCount = myLost
            },
            CombatSummary = new BeaconCombatSummary
            {
                KillsScored = _kills[perspectiveTeam],
                Deaths = _deaths[perspectiveTeam],
                DamageDealt = _damageDealt[perspectiveTeam],
                DamageReceived = _damageReceived[perspectiveTeam],
                FistHits = _fistHits[perspectiveTeam],
                PistolHits = _pistolHits[perspectiveTeam],
                RifleHits = _rifleHits[perspectiveTeam],
                HookGrabs = _hookGrabs[perspectiveTeam],
                ParrySuccesses = _parrySuccesses[perspectiveTeam]
            },
            ActionFrequency = aggActions,
            PositionalSummary = new BeaconPositionalSummary
            {
                AvgX = avgX,
                AvgY = avgY,
                TimeGroundedPct = pawnSamples > 0 ? (float)groundedTicks / pawnSamples : 0,
                TimeOnPlatformPct = pawnSamples > 0 ? (float)platformTicks / pawnSamples : 0,
                TimeInBeaconPct = pawnSamples > 0 ? (float)inBeaconTicks / pawnSamples : 0,
                TimeInBasePct = pawnSamples > 0 ? (float)inBaseTicks / pawnSamples : 0,
                AvgDistToNearestEnemy = pawnSamples > 0 ? totalDistToEnemy / pawnSamples : 0
            },
            KeyMoments = keyMoments,
        };
    }

    private List<BeaconKeyMoment> BuildKeyMoments(int perspectiveTeam)
    {
        var moments = new List<BeaconKeyMoment>();
        int myTeamId = perspectiveTeam + 1;

        // First capture
        var firstCap = _captureEvents.FirstOrDefault(e => e.newOwner == myTeamId);
        if (firstCap != default)
        {
            string[] beaconNames = ["Left", "Center", "Right"];
            moments.Add(new BeaconKeyMoment
            {
                Tick = firstCap.tick,
                Event = "first_capture",
                Description = $"Team captured {beaconNames[firstCap.beaconIdx]} beacon"
            });
        }

        // First kill
        var firstKill = _events.FirstOrDefault(e => e.team == perspectiveTeam && e.eventType == "kill");
        if (firstKill != default)
        {
            moments.Add(new BeaconKeyMoment
            {
                Tick = firstKill.tick,
                Event = "first_kill",
                Description = "Team scored first kill"
            });
        }

        // Score milestones (scaled for target score of 150)
        int prevScore = 0;
        foreach (var s in _samples)
        {
            int score = s.Scores[perspectiveTeam];
            if (prevScore < 50 && score >= 50)
                moments.Add(new BeaconKeyMoment { Tick = s.Tick, Event = "score_50", Description = "Team reached 50 points" });
            if (prevScore < 100 && score >= 100)
                moments.Add(new BeaconKeyMoment { Tick = s.Tick, Event = "score_100", Description = "Team reached 100 points" });
            prevScore = score;
        }

        return moments;
    }

    private void CaptureCheckpoint(BeaconMatch match)
    {
        var pawns = new List<ReplayPawn>();
        var hooks = new List<ReplayHook>();

        for (int i = 0; i < match.AllPawns.Length; i++)
        {
            var p = match.AllPawns[i];
            pawns.Add(new ReplayPawn
            {
                T = p.TeamIndex, R = (int)p.Role,
                X = p.Position.X, Y = p.Position.Y,
                Vx = p.Velocity.X, Vy = p.Velocity.Y,
                Hp = p.Health, G = p.IsGrounded,
                D = p.IsDead, V = p.IsVulnerable,
                Pa = p.IsParryActive,
                Wl = p.FistLockoutTicks > 0 || p.PistolLockoutTicks > 0 || p.RifleLockoutTicks > 0
            });

            if (p.Role == PawnRole.Grappler)
            {
                hooks.Add(new ReplayHook
                {
                    Pi = i,
                    S = (int)p.Hook.ChainState,
                    X = p.Hook.Position.X, Y = p.Hook.Position.Y,
                    Ax = p.Hook.AnchorPoint.X, Ay = p.Hook.AnchorPoint.Y,
                    Cl = p.Hook.ChainLength, A = p.Hook.IsAttachedToWorld
                });
            }
        }

        var beacons = new List<ReplayBeacon>();
        foreach (var b in match.Beacons)
        {
            beacons.Add(new ReplayBeacon
            {
                O = b.OwnerTeam, Cp = b.CaptureProgress, Ct = b.IsContested
            });
        }

        var projectiles = new List<ReplayProjectile>();
        foreach (var proj in match.Projectiles)
        {
            projectiles.Add(new ReplayProjectile
            {
                X = proj.Position.X, Y = proj.Position.Y, T = proj.OwnerTeam
            });
        }

        _checkpoints.Add(new BeaconReplayCheckpoint
        {
            T = match.Tick,
            S = [match.Scores[0], match.Scores[1]],
            K = [match.Kills[0], match.Kills[1]],
            B = beacons,
            P = pawns,
            F = hooks,
            Pr = projectiles
        });
    }

    public BeaconReplayData BuildReplayData(List<BtNode>[] teamATrees, List<BtNode>[] teamBTrees, BeaconArena arena, int? matchSeed = null) => new()
    {
        Arena = new BeaconReplayArena
        {
            Width = arena.Width, Height = arena.Height,
            WallThickness = arena.WallThickness,
            PlatformRect = [arena.Platform.Position.X, arena.Platform.Position.Y, arena.Platform.Size.X, arena.Platform.Size.Y],
            BeaconCenters = arena.BeaconZones.Select(z => new[] { z.Center.X, z.Center.Y }).ToArray(),
            BeaconMultipliers = arena.BeaconZones.Select(z => z.PointMultiplier).ToArray(),
            BaseZoneCenters = arena.BaseZones.Select(z => new[] { z.Center.X, z.Center.Y }).ToArray(),
            Modifiers = arena.Modifiers.HasModifiers ? arena.Modifiers : null
        },
        TeamSize = _teamSize,
        CheckpointInterval = CheckpointInterval,
        MatchSeed = matchSeed,
        TeamATrees = teamATrees.Select(t => t).ToList(),
        TeamBTrees = teamBTrees.Select(t => t).ToList(),
        Checkpoints = _checkpoints,
        PistolShots = _replayPistolShots,
        RifleShots = _replayRifleShots,
        HitEvents = _replayHitEvents
    };

    private record TickSample
    {
        public int Tick { get; init; }
        public int[] Scores { get; init; } = [0, 0];
        public Vector2[] PawnPositions { get; init; } = [];
        public bool[] PawnGrounded { get; init; } = [];
        public bool[] PawnDead { get; init; } = [];
        public int[] PawnTeams { get; init; } = [];
        public float[] PawnHealth { get; init; } = [];
    }
}
