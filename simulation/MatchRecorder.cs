// ─────────────────────────────────────────────────────────────────────────────
// MatchRecorder.cs — Captures match events for battle log generation
// ─────────────────────────────────────────────────────────────────────────────
//
// Attach to a Match via Match.Recorder. The match calls RecordAction,
// RecordHit, and RecordTick during Step(). After the match ends, call
// BuildBattleLog(fighterIdx) to get a perspective-relative battle log.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation;

public class MatchRecorder
{
    private readonly string[] _names;
    private readonly string?[] _colors;
    private readonly int _generation;
    private readonly int[] _btVersions;
    private readonly string _matchId;

    // Per-fighter action frequency
    private readonly Dictionary<string, int>[] _actionCounts = [new(), new()];

    // Per-fighter launch-at-opponent counts (for hit accuracy)
    private readonly int[] _launchesAtOpponent = [0, 0];

    // Hit log (absolute: attackerIdx 0 or 1)
    private readonly List<RawHitEvent> _hits = [];

    // Per-tick positional samples
    private readonly List<TickSample> _samples = [];

    // Replay checkpoints (every N ticks)
    private const int CheckpointInterval = 10;
    private readonly List<ReplayCheckpoint> _checkpoints = [];

    // Grapple tracking
    private readonly bool[,] _prevAttached = new bool[2, 2]; // [fighter, fist: 0=left 1=right]
    private readonly int[] _attachCount = [0, 0];
    private readonly int[] _ceilingAttaches = [0, 0];
    private readonly int[] _wallAttaches = [0, 0];
    private readonly int[] _attachedTicks = [0, 0]; // total ticks any fist attached
    private readonly int[] _attachSegments = [0, 0]; // number of attachment segments

    public MatchRecorder(string[] fighterNames, int generation, int[] btVersions, string matchId, string?[]? colors = null)
    {
        _names = fighterNames;
        _colors = colors ?? [null, null];
        _generation = generation;
        _btVersions = btVersions;
        _matchId = matchId;
    }

    // ── Called by Match.Step() ──

    public void RecordAction(int tick, int fighterIdx, string action)
    {
        var counts = _actionCounts[fighterIdx];
        counts.TryGetValue(action, out int count);
        counts[action] = count + 1;

        if (action is "launch_left_at_opponent" or "launch_right_at_opponent")
            _launchesAtOpponent[fighterIdx]++;
    }

    public void RecordHit(int tick, int attackerIdx, string hand, float damage,
        Vector2 pos0, Vector2 pos1)
    {
        _hits.Add(new RawHitEvent(tick, attackerIdx, hand, damage, pos0, pos1));
    }

    public void RecordTick(Match match)
    {
        var f0 = match.Fighter0;
        var f1 = match.Fighter1;

        _samples.Add(new TickSample(
            match.Tick,
            f0.Position, f1.Position,
            f0.IsGrounded, f1.IsGrounded,
            f0.Health, f1.Health,
            f0.Position.DistanceTo(f1.Position)
        ));

        // Replay checkpoints
        if (match.Tick % CheckpointInterval == 0)
            CaptureCheckpoint(match);

        // Track grapple attach/detach transitions
        TrackGrapple(0, f0, match);
        TrackGrapple(1, f1, match);
    }

    private void TrackGrapple(int idx, Fighter f, Match match)
    {
        for (int fistIdx = 0; fistIdx < 2; fistIdx++)
        {
            var fist = fistIdx == 0 ? f.LeftFist : f.RightFist;
            bool wasAttached = _prevAttached[idx, fistIdx];
            bool isAttached = fist.IsAttachedToWorld;

            if (isAttached && !wasAttached)
            {
                // New attachment
                _attachCount[idx]++;
                _attachSegments[idx]++;

                // Classify surface: ceiling vs wall
                var anchor = fist.AnchorPoint;
                float ceilingY = match.Arena.Bounds.Position.Y;
                if (Math.Abs(anchor.Y - ceilingY) < 5f)
                    _ceilingAttaches[idx]++;
                else
                    _wallAttaches[idx]++;
            }

            if (isAttached)
                _attachedTicks[idx]++;

            _prevAttached[idx, fistIdx] = isAttached;
        }
    }

    // ── Build battle log from one fighter's perspective ──

    public BattleLog BuildBattleLog(int perspectiveIdx)
    {
        int oppIdx = 1 - perspectiveIdx;
        int totalTicks = _samples.Count;
        if (totalTicks == 0)
            return new BattleLog { MatchId = _matchId };

        var lastSample = _samples[^1];

        // Damage summary
        float dealt = _hits.Where(h => h.AttackerIdx == perspectiveIdx).Sum(h => h.Damage);
        float received = _hits.Where(h => h.AttackerIdx == oppIdx).Sum(h => h.Damage);
        int hitsLanded = _hits.Count(h => h.AttackerIdx == perspectiveIdx);
        int hitsTaken = _hits.Count(h => h.AttackerIdx == oppIdx);
        int launches = _launchesAtOpponent[perspectiveIdx];
        float accuracy = launches > 0 ? (float)hitsLanded / launches : 0f;

        // Positional summary
        float avgX = 0, avgY = 0, avgDist = 0;
        int groundedTicks = 0, nearOpponentTicks = 0;
        foreach (var s in _samples)
        {
            var pos = perspectiveIdx == 0 ? s.Pos0 : s.Pos1;
            avgX += pos.X;
            avgY += pos.Y;
            avgDist += s.Distance;
            if (perspectiveIdx == 0 ? s.Grounded0 : s.Grounded1) groundedTicks++;
            if (s.Distance < 200) nearOpponentTicks++;
        }
        avgX /= totalTicks;
        avgY /= totalTicks;
        avgDist /= totalTicks;

        // Result
        float fighterHp = perspectiveIdx == 0 ? lastSample.Hp0 : lastSample.Hp1;
        float opponentHp = perspectiveIdx == 0 ? lastSample.Hp1 : lastSample.Hp0;
        MatchResult result;
        if (fighterHp > opponentHp) result = MatchResult.Win;
        else if (fighterHp < opponentHp) result = MatchResult.Loss;
        else result = MatchResult.Draw;

        // Hit log (perspective-relative)
        var hitLog = _hits.Select(h => new HitEvent
        {
            Tick = h.Tick,
            Attacker = h.AttackerIdx == perspectiveIdx ? "fighter" : "opponent",
            Hand = h.Hand,
            Damage = h.Damage,
            FighterPos = perspectiveIdx == 0
                ? [h.Pos0.X, h.Pos0.Y]
                : [h.Pos1.X, h.Pos1.Y],
            OpponentPos = perspectiveIdx == 0
                ? [h.Pos1.X, h.Pos1.Y]
                : [h.Pos0.X, h.Pos0.Y]
        }).ToList();

        // Key moments
        var keyMoments = BuildKeyMoments(perspectiveIdx);

        // Phase breakdown
        var phases = BuildPhases(perspectiveIdx, totalTicks);

        // Grapple stats
        int segs = _attachSegments[perspectiveIdx];
        float avgAttachDur = segs > 0 ? (float)_attachedTicks[perspectiveIdx] / segs : 0;

        var fighterPos = perspectiveIdx == 0 ? lastSample.Pos0 : lastSample.Pos1;
        var opponentPos = perspectiveIdx == 0 ? lastSample.Pos1 : lastSample.Pos0;

        return new BattleLog
        {
            MatchId = _matchId,
            Generation = _generation,
            Fighter = _names[perspectiveIdx],
            FighterColor = _colors[perspectiveIdx],
            Opponent = _names[oppIdx],
            OpponentColor = _colors[oppIdx],
            FighterBtVersion = _btVersions[perspectiveIdx],
            OpponentBtVersion = _btVersions[oppIdx],
            Result = result,
            DurationTicks = totalTicks,
            DurationSeconds = totalTicks * SimPhysics.FixedDt,
            FinalState = new FinalStateData
            {
                FighterHp = fighterHp,
                OpponentHp = opponentHp,
                FighterPos = [fighterPos.X, fighterPos.Y],
                OpponentPos = [opponentPos.X, opponentPos.Y]
            },
            DamageSummary = new DamageSummaryData
            {
                Dealt = dealt,
                Received = received,
                HitsLanded = hitsLanded,
                HitsTaken = hitsTaken,
                LaunchesAtOpponent = launches,
                HitAccuracy = accuracy
            },
            ActionFrequency = new Dictionary<string, int>(_actionCounts[perspectiveIdx]),
            PositionalSummary = new PositionalSummaryData
            {
                AvgX = avgX,
                AvgY = avgY,
                TimeGroundedPct = (float)groundedTicks / totalTicks,
                TimeAirbornePct = 1f - (float)groundedTicks / totalTicks,
                TimeNearOpponentPct = (float)nearOpponentTicks / totalTicks,
                AvgDistanceToOpponent = avgDist
            },
            GrappleStats = new GrappleStatsData
            {
                AttachCount = _attachCount[perspectiveIdx],
                AvgAttachedDurationTicks = avgAttachDur,
                CeilingAttaches = _ceilingAttaches[perspectiveIdx],
                WallAttaches = _wallAttaches[perspectiveIdx]
            },
            PhaseBreakdown = phases,
            HitLog = hitLog,
            KeyMoments = keyMoments
        };
    }

    // ── Phase breakdown ──

    private List<PhaseData> BuildPhases(int perspectiveIdx, int totalTicks)
    {
        int oppIdx = 1 - perspectiveIdx;
        string[] phaseNames = ["early", "mid", "late"];
        int phaseLen = totalTicks / 3;
        var phases = new List<PhaseData>();

        for (int p = 0; p < 3; p++)
        {
            int start = p * phaseLen;
            int end = p == 2 ? totalTicks : (p + 1) * phaseLen;

            float dmgDealt = _hits
                .Where(h => h.AttackerIdx == perspectiveIdx && h.Tick >= start && h.Tick < end)
                .Sum(h => h.Damage);
            float dmgReceived = _hits
                .Where(h => h.AttackerIdx == oppIdx && h.Tick >= start && h.Tick < end)
                .Sum(h => h.Damage);

            // Count actions in this phase
            var phaseActions = new Dictionary<string, int>();
            // We don't have per-tick action logs, so use proportional estimate from totals
            // (action frequency is aggregate — phases get dominant actions from hit patterns)

            // Get HP at end of phase
            float hp0 = 100f, hp1 = 100f;
            if (end - 1 < _samples.Count)
            {
                var s = _samples[end - 1];
                hp0 = s.Hp0;
                hp1 = s.Hp1;
            }

            // Dominant actions: top 3 from overall (we track aggregate, not per-phase)
            var dominant = _actionCounts[perspectiveIdx]
                .OrderByDescending(kv => kv.Value)
                .Take(3)
                .Select(kv => kv.Key)
                .ToList();

            phases.Add(new PhaseData
            {
                Phase = phaseNames[p],
                TickRange = [start, end],
                DamageDealt = dmgDealt,
                DamageReceived = dmgReceived,
                DominantActions = dominant,
                HpAtEnd = perspectiveIdx == 0 ? [hp0, hp1] : [hp1, hp0]
            });
        }

        return phases;
    }

    // ── Key moments ──

    private List<KeyMoment> BuildKeyMoments(int perspectiveIdx)
    {
        var moments = new List<KeyMoment>();

        // First blood
        if (_hits.Count > 0)
        {
            var first = _hits[0];
            string who = first.AttackerIdx == perspectiveIdx ? "Fighter" : "Opponent";
            moments.Add(new KeyMoment
            {
                Tick = first.Tick,
                Event = "first_blood",
                Description = $"{who} landed first hit with {first.Hand} fist"
            });
        }

        // Health crossovers (lead changes)
        float prevDelta = 0;
        foreach (var s in _samples)
        {
            float fHp = perspectiveIdx == 0 ? s.Hp0 : s.Hp1;
            float oHp = perspectiveIdx == 0 ? s.Hp1 : s.Hp0;
            float delta = fHp - oHp;

            if (prevDelta <= 0 && delta > 0 && s.Tick > 0)
            {
                moments.Add(new KeyMoment
                {
                    Tick = s.Tick,
                    Event = "health_crossover",
                    Description = $"Fighter took the lead: {fHp:F0} vs {oHp:F0} HP"
                });
            }
            else if (prevDelta >= 0 && delta < 0 && s.Tick > 0)
            {
                moments.Add(new KeyMoment
                {
                    Tick = s.Tick,
                    Event = "health_crossover",
                    Description = $"Opponent took the lead: {oHp:F0} vs {fHp:F0} HP"
                });
            }
            prevDelta = delta;
        }

        // Knockout
        if (_samples.Count > 0)
        {
            var last = _samples[^1];
            float fHp = perspectiveIdx == 0 ? last.Hp0 : last.Hp1;
            float oHp = perspectiveIdx == 0 ? last.Hp1 : last.Hp0;

            if (oHp <= 0 && fHp > 0)
            {
                var killHit = _hits.LastOrDefault(h => h.AttackerIdx == perspectiveIdx);
                string hand = killHit?.Hand ?? "unknown";
                moments.Add(new KeyMoment
                {
                    Tick = last.Tick,
                    Event = "knockout",
                    Description = $"Fighter KO'd opponent with {hand} fist"
                });
            }
            else if (fHp <= 0 && oHp > 0)
            {
                moments.Add(new KeyMoment
                {
                    Tick = last.Tick,
                    Event = "knocked_out",
                    Description = $"Fighter was KO'd by opponent"
                });
            }
        }

        return moments;
    }

    // ── Replay ──

    private void CaptureCheckpoint(Match match)
    {
        var f0 = match.Fighter0;
        var f1 = match.Fighter1;

        _checkpoints.Add(new ReplayCheckpoint
        {
            T = match.Tick,
            F = [
                new ReplayFighter { X = f0.Position.X, Y = f0.Position.Y, Vx = f0.Velocity.X, Vy = f0.Velocity.Y, Hp = f0.Health, G = f0.IsGrounded },
                new ReplayFighter { X = f1.Position.X, Y = f1.Position.Y, Vx = f1.Velocity.X, Vy = f1.Velocity.Y, Hp = f1.Health, G = f1.IsGrounded }
            ],
            Fists = [
                CaptureFist(f0.LeftFist),
                CaptureFist(f0.RightFist),
                CaptureFist(f1.LeftFist),
                CaptureFist(f1.RightFist)
            ]
        });
    }

    private static ReplayFist CaptureFist(Fist fist) => new()
    {
        S = (int)fist.ChainState,
        X = fist.Position.X,
        Y = fist.Position.Y,
        Ax = fist.AnchorPoint.X,
        Ay = fist.AnchorPoint.Y,
        Cl = fist.ChainLength,
        A = fist.IsAttachedToWorld
    };

    public ReplayData BuildReplayData(List<BtNode> trees0, List<BtNode> trees1, Arena arena, int? matchSeed = null) => new()
    {
        Arena = new ReplayArena { Width = arena.Width, Height = arena.Height, WallThickness = arena.WallThickness },
        CheckpointInterval = CheckpointInterval,
        MatchSeed = matchSeed,
        FighterTrees = [trees0, trees1],
        Checkpoints = _checkpoints
    };

    // ── Internal data ──

    private record RawHitEvent(int Tick, int AttackerIdx, string Hand, float Damage,
        Vector2 Pos0, Vector2 Pos1);

    private record TickSample(int Tick, Vector2 Pos0, Vector2 Pos1,
        bool Grounded0, bool Grounded1, float Hp0, float Hp1, float Distance);
}
