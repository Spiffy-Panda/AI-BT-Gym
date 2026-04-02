// ─────────────────────────────────────────────────────────────────────────────
// MovementTests.cs — Headless movement tests for BT-driven fighters
// ─────────────────────────────────────────────────────────────────────────────
//
// Each test places a single fighter in the standard arena (5000x400) with a
// dummy opponent parked far away. A purpose-built BT runs for N ticks and we
// assert the fighter moved in a predictable pattern.
//
// Fists anchor mid-air when locked — no surface contact needed.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.When;

namespace AiBtGym.Tests;

public partial class MovementTests : Node
{
    public override void _Ready()
    {
        var results = RunAll();

        GD.Print("═══════════════════════════════════════");
        GD.Print("  Movement Tests");
        GD.Print("═══════════════════════════════════════");

        int passed = 0, failed = 0;
        var failures = new List<string>();
        foreach (var r in results)
        {
            if (r.Passed) { GD.Print($"  [PASS] {r.Name}"); passed++; }
            else { GD.Print($"  [FAIL] {r.Name}: {r.Error}"); failed++; failures.Add($"{r.Name}: {r.Error}"); }
        }

        GD.Print("═══════════════════════════════════════");
        GD.Print($"  Results: {passed} passed, {failed} failed");
        if (failures.Count > 0) { GD.Print("  Failures:"); foreach (var f in failures) GD.Print($"    - {f}"); }
        GD.Print("═══════════════════════════════════════");
        GD.Print();

        // Run map self-play tests
        var mapResults = MapTests.RunAll();
        int mapFailed = MapTests.PrintResults(mapResults);

        // Run beacon brawl map tests
        var bbResults = BeaconMapTests.RunAll();
        int bbFailed = BeaconMapTests.PrintResults(bbResults);

        // Run informed vs uninformed experiments
        var matchupResults = BeaconMapTests.RunInformedVsUninformed(gamesPerMatchup: 10);
        BeaconMapTests.PrintMatchupResults(matchupResults);

        GetTree().Quit((failed + mapFailed + bbFailed) > 0 ? 1 : 0);
    }

    // ── Public API ──

    public static List<TestResult> RunAll()
    {
        var tests = new (string name, Func<TestResult> run)[]
        {
            ("WalkRight", TestWalkRight),
            ("WalkLeft", TestWalkLeft),
            ("JumpAndLand", TestJumpAndLand),
            ("MidAirGrapple", TestMidAirGrapple),
            ("PendulumSwing", TestPendulumSwing),
            ("GrapplePull", TestGrapplePull),
            ("AirborneViaAlternatingGrapple", TestAirborneAlternating),
            ("GravityFallOnly", TestGravityFall),
            ("SpiderManSwing", TestSpiderManSwing),
        };

        var results = new List<TestResult>();
        foreach (var (name, run) in tests)
        {
            try { results.Add(run()); }
            catch (Exception e) { results.Add(new TestResult { Name = name, Passed = false, Error = e.Message }); }
        }
        return results;
    }

    // ── Infrastructure ──

    private static void Assert(bool condition, string msg)
    {
        if (!condition) throw new Exception(msg);
    }

    private static void TickFighter(Fighter fighter, Arena arena)
    {
        var pos = fighter.Position;
        var vel = fighter.Velocity;
        SimPhysics.Integrate(ref pos, ref vel, SimPhysics.FixedDt, fighter.IsGrounded);
        fighter.Position = pos;
        fighter.Velocity = vel;
        SimPhysics.ApplyChainConstraint(fighter, fighter.LeftFist, SimPhysics.FixedDt);
        SimPhysics.ApplyChainConstraint(fighter, fighter.RightFist, SimPhysics.FixedDt);
        pos = fighter.Position;
        vel = fighter.Velocity;
        arena.ClampToArena(ref pos, ref vel, fighter.BodyRadius);
        fighter.Position = pos;
        fighter.Velocity = vel;
        fighter.IsGrounded = arena.IsOnGround(fighter.Position, fighter.BodyRadius);

        // Grounding override: being pulled by grapple = airborne
        if ((fighter.LeftFist.IsAttachedToWorld && fighter.LeftFist.ChainState == FistChainState.Retracting) ||
            (fighter.RightFist.IsAttachedToWorld && fighter.RightFist.ChainState == FistChainState.Retracting))
            fighter.IsGrounded = false;

        fighter.LeftFist.Tick(SimPhysics.FixedDt, fighter.Position);
        fighter.RightFist.Tick(SimPhysics.FixedDt, fighter.Position);
    }

    private static ReplayCheckpoint CaptureCheckpoint(int tick, Fighter fighter, Fighter dummy) => new()
    {
        T = tick,
        F = [
            new ReplayFighter { X = fighter.Position.X, Y = fighter.Position.Y, Vx = fighter.Velocity.X, Vy = fighter.Velocity.Y, Hp = fighter.Health, G = fighter.IsGrounded },
            new ReplayFighter { X = dummy.Position.X, Y = dummy.Position.Y, Vx = dummy.Velocity.X, Vy = dummy.Velocity.Y, Hp = dummy.Health, G = dummy.IsGrounded }
        ],
        Fists = [
            CaptureFist(fighter.LeftFist), CaptureFist(fighter.RightFist),
            CaptureFist(dummy.LeftFist), CaptureFist(dummy.RightFist)
        ]
    };

    private static ReplayFist CaptureFist(Fist fist) => new()
    {
        S = (int)fist.ChainState, X = fist.Position.X, Y = fist.Position.Y,
        Ax = fist.AnchorPoint.X, Ay = fist.AnchorPoint.Y, Cl = fist.ChainLength, A = fist.IsAttachedToWorld
    };

    /// <summary>
    /// Run a single fighter with a BT for N ticks, capturing every tick.
    /// Fighter starts at far left of arena. Standard arena is 5000x400.
    /// </summary>
    private static (Fighter fighter, List<(int tick, Vector2 pos)> samples, List<ReplayCheckpoint> checkpoints, Arena arena)
        RunRecorded(List<BtNode> bt, int ticks, int sampleEvery = 60,
            float arenaW = 5000f, float arenaH = 400f,
            Vector2? fighterStart = null, Vector2? fighterVelocity = null)
    {
        var arena = new Arena(arenaW, arenaH);
        float groundY = arena.Bounds.End.Y - 20f;
        float leftSpawn = arena.Bounds.Position.X + 50f;
        var fighter = new Fighter(0, fighterStart ?? new Vector2(leftSpawn, groundY));
        var dummy = new Fighter(1, new Vector2(arenaW - 100f, groundY));
        if (fighterVelocity.HasValue) fighter.Velocity = fighterVelocity.Value;
        var runner = new BehaviorTreeRunner(bt);
        var dummyBt = new BehaviorTreeRunner(new List<BtNode> { Act("noop") });

        var samples = new List<(int, Vector2)>();
        var checkpoints = new List<ReplayCheckpoint>();

        for (int t = 0; t < ticks; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            var dCtx = new FighterBtContext(dummy, fighter, arena, t);
            runner.Apply(ctx);
            dummyBt.Apply(dCtx);
            TickFighter(fighter, arena);
            checkpoints.Add(CaptureCheckpoint(t, fighter, dummy));
            if (t % sampleEvery == 0) samples.Add((t, fighter.Position));
        }
        return (fighter, samples, checkpoints, arena);
    }

    private static TestResult MakeResult(string name, bool passed, string? error, int ticks, List<ReplayCheckpoint> checkpoints, Arena arena) => new()
    {
        Name = name, Passed = passed, Error = error, DurationTicks = ticks,
        Replay = new ReplayData
        {
            Arena = new ReplayArena { Width = arena.Width, Height = arena.Height, WallThickness = arena.WallThickness },
            CheckpointInterval = 1, Checkpoints = checkpoints
        }
    };

    // ═══════════════════════════════════════════════════════════════════════
    // TESTS
    // ═══════════════════════════════════════════════════════════════════════

    private static TestResult TestWalkRight()
    {
        var bt = new List<BtNode> { Act("move_right") };
        var (_, samples, cps, arena) = RunRecorded(bt, 300, sampleEvery: 60);
        for (int i = 1; i < samples.Count; i++)
            Assert(samples[i].pos.X > samples[i - 1].pos.X, $"X should increase: tick {samples[i].tick}");
        Assert(samples[^1].pos.X - samples[0].pos.X > 100f, "Should have moved significantly right");
        return MakeResult("WalkRight", true, null, 300, cps, arena);
    }

    private static TestResult TestWalkLeft()
    {
        var bt = new List<BtNode> { Act("move_left") };
        // Start mid-arena so there's room to walk left
        var (_, samples, cps, arena) = RunRecorded(bt, 300, sampleEvery: 60,
            fighterStart: new Vector2(2500f, 370f));
        for (int i = 1; i < samples.Count; i++)
            Assert(samples[i].pos.X < samples[i - 1].pos.X, $"X should decrease: tick {samples[i].tick}");
        return MakeResult("WalkLeft", true, null, 300, cps, arena);
    }

    private static TestResult TestJumpAndLand()
    {
        var bt = new List<BtNode> { Seq(Cond(Grounded), Act("jump")) };
        var (_, samples, cps, arena) = RunRecorded(bt, 600, sampleEvery: 10);
        float startY = samples[0].pos.Y;
        bool wentUp = false, cameBack = false;
        foreach (var (_, pos) in samples)
        {
            if (pos.Y < startY - 30f) wentUp = true;
            if (wentUp && pos.Y >= startY - 5f) cameBack = true;
        }
        Assert(wentUp, "Fighter never left the ground");
        Assert(cameBack, "Fighter never returned to ground");
        int crossings = 0;
        for (int i = 1; i < samples.Count; i++)
            if (samples[i - 1].pos.Y >= startY - 2f && samples[i].pos.Y < startY - 2f) crossings++;
        Assert(crossings >= 3, $"Expected at least 3 jump cycles, got {crossings}");
        return MakeResult("JumpAndLand", true, null, 600, cps, arena);
    }

    /// <summary>
    /// Mid-air grapple: launch fist up, lock mid-air (no surface needed),
    /// retract to pull upward. Fighter should gain significant altitude.
    /// </summary>
    private static TestResult TestMidAirGrapple()
    {
        var bt = new List<BtNode>
        {
            Sel(
                // Lock extending fist once chain is long enough (mid-air anchor)
                Seq(Cond(RightExtending), Cond(RightChainOver(100)), Act("lock_right")),
                // If locked+attached, retract to pull up
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // If retracted, launch up again
                Seq(Cond(RightReady), Act("launch_right_up")),
                Act("move_right")
            )
        };

        var (_, _, cps, arena) = RunRecorded(bt, 300);
        float startY = cps[0].F[0].Y;
        float minY = cps.Min(c => c.F[0].Y);
        float altitude = startY - minY;
        Assert(altitude > 50f, $"Expected significant upward movement, altitude gain was only {altitude:F0}px");
        return MakeResult("MidAirGrapple", true, null, 300, cps, arena);
    }

    /// <summary>
    /// Pendulum swing: launch fist up, lock mid-air, swing. X should oscillate.
    /// </summary>
    private static TestResult TestPendulumSwing()
    {
        var bt = new List<BtNode>
        {
            Sel(
                Seq(Cond(RightExtending), Cond(RightChainOver(80)), Act("lock_right")),
                Seq(Cond(RightReady), Act("launch_right_up"))
            )
        };

        var (_, samples, cps, arena) = RunRecorded(bt, 600, sampleEvery: 10,
            fighterStart: new Vector2(400f, 200f),
            fighterVelocity: new Vector2(300f, 0));

        int reversals = 0;
        for (int i = 2; i < samples.Count - 1; i++)
        {
            float dx1 = samples[i].pos.X - samples[i - 1].pos.X;
            float dx2 = samples[i + 1].pos.X - samples[i].pos.X;
            if ((dx1 > 2f && dx2 < -2f) || (dx1 < -2f && dx2 > 2f)) reversals++;
        }
        Assert(reversals >= 2, $"Expected oscillating X motion (pendulum), got {reversals} reversals");
        return MakeResult("PendulumSwing", true, null, 600, cps, arena);
    }

    /// <summary>
    /// Grapple pull: launch fist forward, lock mid-air, retract to pull.
    /// Fighter should move horizontally toward the anchor point.
    /// </summary>
    private static TestResult TestGrapplePull()
    {
        var bt = new List<BtNode>
        {
            Sel(
                Seq(Cond(RightExtending), Cond(RightChainOver(150)), Act("lock_right")),
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                Seq(Cond(RightReady), Act("launch_right_upright"))
            )
        };

        var (_, samples, cps, arena) = RunRecorded(bt, 600, sampleEvery: 10);
        float minX = samples.Min(s => s.pos.X);
        float maxX = samples.Max(s => s.pos.X);
        float range = maxX - minX;
        Assert(range > 40f, $"Expected horizontal traversal via grapple pull, X range was only {range:F0}px");
        return MakeResult("GrapplePull", true, null, 600, cps, arena);
    }

    /// <summary>
    /// Alternating grapple to stay airborne: launch fists up, lock mid-air,
    /// retract to pull up, repeat. Should stay above ground most of the time.
    /// </summary>
    private static TestResult TestAirborneAlternating()
    {
        var bt = new List<BtNode>
        {
            Sel(
                // Lock extending fists mid-air
                Seq(Cond(LeftExtending), Cond(LeftChainOver(80)), Act("lock_left")),
                Seq(Cond(RightExtending), Cond(RightChainOver(80)), Act("lock_right")),
                // Retract locked+attached fists (pull up)
                Seq(Cond(LeftAnchored), Cond(LeftLocked), Act("retract_left")),
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // Launch free fists upward
                Seq(Cond(LeftReady), Act("launch_left_up")),
                Seq(Cond(RightReady), Act("launch_right_up"))
            )
        };

        var (_, samples, cps, arena) = RunRecorded(bt, 600, sampleEvery: 30);
        float groundY = samples[0].pos.Y;

        int aboveGroundCount = 0, totalLate = 0;
        foreach (var (tick, pos) in samples)
        {
            if (tick < 180) continue;
            totalLate++;
            if (pos.Y < groundY - 30f) aboveGroundCount++;
        }
        Assert(totalLate > 0, "Not enough late samples");
        float pct = (float)aboveGroundCount / totalLate;
        Assert(pct > 0.3f, $"Expected airborne >30% after 3s, was {pct * 100:F0}%");
        return MakeResult("AirborneViaAlternatingGrapple", true, null, 600, cps, arena);
    }

    /// <summary>
    /// Control test: no actions, fighter falls and stays on ground.
    /// </summary>
    private static TestResult TestGravityFall()
    {
        var bt = new List<BtNode> { Cond(Var.Never) };
        var (_, _, cps, arena) = RunRecorded(bt, 300, sampleEvery: 10,
            fighterStart: new Vector2(500f, 100f));

        bool hitGround = false;
        int groundTick = -1;
        foreach (var cp in cps)
        {
            if (!hitGround && cp.F[0].G) { hitGround = true; groundTick = cp.T; }
        }
        Assert(hitGround, "Fighter never hit the ground under gravity");
        Assert(groundTick < 120, $"Took too long to hit ground: {groundTick} ticks");
        float finalX = cps[^1].F[0].X;
        Assert(Mathf.Abs(finalX - 500f) < 5f, $"Fighter drifted horizontally: end={finalX:F0}");
        Assert(cps[^1].F[0].G, "Fighter should be grounded at end");
        return MakeResult("GravityFallOnly", true, null, 300, cps, arena);
    }

    /// <summary>
    /// Spider-Man swing: sequential mid-air swings for horizontal traversal.
    /// Launch fist up, lock mid-air, swing forward, release, repeat.
    /// Should traverse >1000px in 600 ticks.
    /// </summary>
    private static TestResult TestSpiderManSwing()
    {
        var bt = new List<BtNode>
        {
            Sel(
                // Lock extending fist mid-air once chain is long enough
                Seq(Cond(RightExtending), Cond(RightChainOver(100)), Act("lock_right")),
                // Swing from anchor, then retract to release
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // Launch fist upward to create next anchor
                Seq(Cond(RightReady), Act("launch_right_up")),
                // Always push right for momentum
                Act("move_right")
            )
        };

        var (_, _, cps, arena) = RunRecorded(bt, 600);
        float startX = cps[0].F[0].X;
        float endX = cps[^1].F[0].X;
        float traversed = endX - startX;
        Assert(traversed > 500f, $"Expected >500px horizontal traversal via swing, got {traversed:F0}px");
        return MakeResult("SpiderManSwing", true, null, 600, cps, arena);
    }
}
