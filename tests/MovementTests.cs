// ─────────────────────────────────────────────────────────────────────────────
// MovementTests.cs — Headless movement tests for BT-driven fighters
// ─────────────────────────────────────────────────────────────────────────────
//
// Each test places a single fighter in a large arena with a dummy opponent
// parked far away. A purpose-built movement BT runs for N ticks and we
// assert the fighter moved in a predictable, perpetual pattern.
//
// Run: open test_runner.tscn in Godot (or pass --scene res://scenes/test_runner.tscn)

using System;
using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.When;

namespace AiBtGym.Tests;

public partial class MovementTests : Node
{
    private int _passed;
    private int _failed;
    private readonly List<string> _failures = new();

    public override void _Ready()
    {
        GD.Print("═══════════════════════════════════════");
        GD.Print("  Movement Tests");
        GD.Print("═══════════════════════════════════════");

        RunTest("WalkRight", TestWalkRight);
        RunTest("WalkLeft", TestWalkLeft);
        RunTest("JumpAndLand", TestJumpAndLand);
        RunTest("CeilingGrapple", TestCeilingGrapple);
        RunTest("PendulumSwing", TestPendulumSwing);
        RunTest("WallGrappleLoop", TestWallGrappleLoop);
        RunTest("AirborneViaAlternatingGrapple", TestAirborneAlternating);
        RunTest("GravityFallOnly", TestGravityFall);

        GD.Print("═══════════════════════════════════════");
        GD.Print($"  Results: {_passed} passed, {_failed} failed");
        if (_failures.Count > 0)
        {
            GD.Print("  Failures:");
            foreach (var f in _failures) GD.Print($"    - {f}");
        }
        GD.Print("═══════════════════════════════════════");

        // Exit with code
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    // ── Test infrastructure ──

    private void RunTest(string name, Action test)
    {
        try
        {
            test();
            GD.Print($"  [PASS] {name}");
            _passed++;
        }
        catch (Exception e)
        {
            GD.Print($"  [FAIL] {name}: {e.Message}");
            _failed++;
            _failures.Add($"{name}: {e.Message}");
        }
    }

    private static void Assert(bool condition, string msg)
    {
        if (!condition) throw new Exception(msg);
    }

    /// <summary>Wire up surface check callbacks for a fighter's fists.</summary>
    private static void WireSurfaceChecks(Fighter f, Arena arena)
    {
        (bool, Vector2) Check(Vector2 pos) =>
            arena.TryGetNearestSurface(pos, out var pt) ? (true, pt) : (false, Vector2.Zero);
        f.LeftFist.SurfaceCheck = Check;
        f.RightFist.SurfaceCheck = Check;
    }

    /// <summary>Shared physics tick for a single fighter.</summary>
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
        fighter.LeftFist.Tick(SimPhysics.FixedDt, fighter.Position);
        fighter.RightFist.Tick(SimPhysics.FixedDt, fighter.Position);
    }

    /// <summary>
    /// Run a single fighter with a BT for N ticks in a large arena.
    /// Dummy opponent sits at (5000, groundY) doing nothing.
    /// Returns the fighter after simulation.
    /// </summary>
    private static Fighter RunSolo(List<BtNode> bt, int ticks, float arenaW = 10000f, float arenaH = 2000f)
    {
        var arena = new Arena(arenaW, arenaH);
        float groundY = arena.Bounds.End.Y - 20f;
        var fighter = new Fighter(0, new Vector2(arenaW / 2f, groundY));
        var dummy = new Fighter(1, new Vector2(arenaW - 100f, groundY));
        var runner = new BehaviorTreeRunner(bt);
        var dummyBt = new BehaviorTreeRunner(new List<BtNode> { Act("noop") });
        WireSurfaceChecks(fighter, arena);

        for (int t = 0; t < ticks; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            var dCtx = new FighterBtContext(dummy, fighter, arena, t);
            runner.Apply(ctx);
            dummyBt.Apply(dCtx);

            TickFighter(fighter, arena);
        }
        return fighter;
    }

    /// <summary>
    /// Run and sample position at regular intervals. Returns list of (tick, pos).
    /// </summary>
    private static List<(int tick, Vector2 pos)> RunAndSample(
        List<BtNode> bt, int ticks, int sampleEvery = 60,
        float arenaW = 10000f, float arenaH = 2000f)
    {
        var arena = new Arena(arenaW, arenaH);
        float groundY = arena.Bounds.End.Y - 20f;
        var fighter = new Fighter(0, new Vector2(arenaW / 2f, groundY));
        var dummy = new Fighter(1, new Vector2(arenaW - 100f, groundY));
        var runner = new BehaviorTreeRunner(bt);
        var dummyBt = new BehaviorTreeRunner(new List<BtNode> { Act("noop") });
        WireSurfaceChecks(fighter, arena);
        var samples = new List<(int, Vector2)>();

        for (int t = 0; t < ticks; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            var dCtx = new FighterBtContext(dummy, fighter, arena, t);
            runner.Apply(ctx);
            dummyBt.Apply(dCtx);

            TickFighter(fighter, arena);

            if (t % sampleEvery == 0)
                samples.Add((t, fighter.Position));
        }
        return samples;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TESTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walk right: X should increase monotonically over time.
    /// </summary>
    private void TestWalkRight()
    {
        var bt = new List<BtNode> { Act("move_right") };
        var samples = RunAndSample(bt, 300, sampleEvery: 60); // 5 seconds

        for (int i = 1; i < samples.Count; i++)
        {
            Assert(samples[i].pos.X > samples[i - 1].pos.X,
                $"X should increase: tick {samples[i].tick} ({samples[i].pos.X:F0}) <= tick {samples[i - 1].tick} ({samples[i - 1].pos.X:F0})");
        }

        float totalDx = samples[^1].pos.X - samples[0].pos.X;
        Assert(totalDx > 100f, $"Should have moved significantly right, only moved {totalDx:F0}px");
    }

    /// <summary>
    /// Walk left: X should decrease monotonically.
    /// </summary>
    private void TestWalkLeft()
    {
        var bt = new List<BtNode> { Act("move_left") };
        var samples = RunAndSample(bt, 300, sampleEvery: 60);

        for (int i = 1; i < samples.Count; i++)
        {
            Assert(samples[i].pos.X < samples[i - 1].pos.X,
                $"X should decrease: tick {samples[i].tick} ({samples[i].pos.X:F0}) >= tick {samples[i - 1].tick} ({samples[i - 1].pos.X:F0})");
        }
    }

    /// <summary>
    /// Jump and land: fighter should leave ground, reach peak, return to ground.
    /// Repeating jump-on-landing should produce a consistent bouncing pattern.
    /// </summary>
    private void TestJumpAndLand()
    {
        // Jump whenever grounded
        var bt = new List<BtNode>
        {
            Seq(Cond(Grounded), Act("jump"))
        };

        var samples = RunAndSample(bt, 600, sampleEvery: 10); // 10 seconds, fine sampling
        float startY = samples[0].pos.Y;

        // Should have gone above start at some point
        bool wentUp = false;
        bool cameBack = false;
        foreach (var (tick, pos) in samples)
        {
            if (pos.Y < startY - 30f) wentUp = true;
            if (wentUp && pos.Y >= startY - 5f) cameBack = true;
        }

        Assert(wentUp, $"Fighter never left the ground (startY={startY:F0})");
        Assert(cameBack, "Fighter never returned to ground after jumping");

        // Check periodicity: count how many times we cross the start Y going up
        int crossings = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i - 1].pos.Y >= startY - 2f && samples[i].pos.Y < startY - 2f)
                crossings++;
        }
        Assert(crossings >= 3, $"Expected at least 3 jump cycles in 10s, got {crossings}");
    }

    /// <summary>
    /// Ceiling grapple: launch fist up, it auto-attaches to ceiling,
    /// retract to pull upward. Fighter should gain significant altitude.
    /// Arena must be small enough that max chain length (280px) reaches ceiling.
    /// </summary>
    private void TestCeilingGrapple()
    {
        var bt = new List<BtNode>
        {
            Sel(
                // If attached and extending, lock it
                Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
                // If attached and locked, retract to pull up
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // If retracted, launch up again
                Seq(Cond(RightReady), Act("launch_right_up")),
                // Fallback: drift right
                Act("move_right")
            )
        };

        // arenaH=330 so ceiling (~10) is ~290px from ground (~300), within 280px chain + 20px attach radius
        var arena = new Arena(10000f, 330f);
        float groundY2 = arena.Bounds.End.Y - 20f;
        var fighter = new Fighter(0, new Vector2(5000f, groundY2));
        var dummy = new Fighter(1, new Vector2(9900f, groundY2));
        var runner = new BehaviorTreeRunner(bt);
        var dummyBt2 = new BehaviorTreeRunner(new List<BtNode> { Act("noop") });
        WireSurfaceChecks(fighter, arena);
        float startY = fighter.Position.Y;
        float minY = startY;

        for (int t = 0; t < 300; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            runner.Apply(ctx);
            TickFighter(fighter, arena);

            if (fighter.Position.Y < minY) minY = fighter.Position.Y;
        }

        float altitude = startY - minY;
        Assert(altitude > 50f, $"Expected significant upward movement, best altitude gain was only {altitude:F0}px");
    }

    /// <summary>
    /// Pendulum swing: attach to ceiling, lock, swing. X should oscillate
    /// back and forth around the anchor point.
    /// </summary>
    private void TestPendulumSwing()
    {
        // Launch fist up, lock on attach, let gravity swing.
        // Short chain (fighter near ceiling) for fast period that outruns friction.
        var bt = new List<BtNode>
        {
            Sel(
                Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
                Seq(Cond(RightReady), Act("launch_right_up"))
            )
        };

        // Fighter near ceiling → short chain → fast pendulum period
        // Arena ceiling at Y=10, fighter at Y=120 → chain ~110px
        // Period = 2π√(110/980) ≈ 2.1s = 126 ticks
        var arena = new Arena(800f, 400f);
        var fighter = new Fighter(0, new Vector2(400f, 120f));
        var dummy = new Fighter(1, new Vector2(700f, arena.Bounds.End.Y - 20f));
        var runner = new BehaviorTreeRunner(bt);
        WireSurfaceChecks(fighter, arena);

        // Strong initial horizontal velocity to create displacement
        fighter.Velocity = new Vector2(300f, 0);

        var samples = new List<(int tick, Vector2 pos)>();
        for (int t = 0; t < 600; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            runner.Apply(ctx);
            TickFighter(fighter, arena);
            if (t % 10 == 0) samples.Add((t, fighter.Position));
        }

        // After attachment (~10 ticks), X should oscillate.
        // Detect reversals: X was increasing then decreasing (or vice versa).
        int reversals = 0;
        for (int i = 2; i < samples.Count - 1; i++)
        {
            float dx1 = samples[i].pos.X - samples[i - 1].pos.X;
            float dx2 = samples[i + 1].pos.X - samples[i].pos.X;
            if ((dx1 > 2f && dx2 < -2f) || (dx1 < -2f && dx2 > 2f))
                reversals++;
        }

        Assert(reversals >= 2, $"Expected oscillating X motion (pendulum), got {reversals} reversals");
    }

    /// <summary>
    /// Wall grapple loop: launch fists at walls, grapple pull.
    /// Fighter should traverse horizontally.
    /// Arena 400px wide — walls are within 280px chain reach from center.
    /// </summary>
    private void TestWallGrappleLoop()
    {
        // Simple: always launch right fist rightward, grapple when attached.
        // Fighter gets pulled right. Then we check it moved.
        var bt = new List<BtNode>
        {
            Sel(
                // If right fist attached, lock then retract to pull toward wall
                Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // Launch right fist toward right wall
                Seq(Cond(RightReady), Act("launch_right_wall"))
            )
        };

        var samples = RunAndSample(bt, 600, sampleEvery: 10, arenaW: 400f, arenaH: 330f);

        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (var (_, pos) in samples)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.X > maxX) maxX = pos.X;
        }

        float range = maxX - minX;
        Assert(range > 40f, $"Expected horizontal traversal via grapple, X range was only {range:F0}px");
    }

    /// <summary>
    /// Alternating grapple to stay airborne: launch one fist up, grapple,
    /// launch other fist up, grapple. Fighter should stay above ground level
    /// after initial ascent.
    /// </summary>
    private void TestAirborneAlternating()
    {
        var bt = new List<BtNode>
        {
            Sel(
                // Lock attached fists
                Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),
                Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
                // Retract locked+attached fists (pull up)
                Seq(Cond(LeftAnchored), Cond(LeftLocked), Act("retract_left")),
                Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                // Launch free fists upward
                Seq(Cond(LeftReady), Act("launch_left_up")),
                Seq(Cond(RightReady), Act("launch_right_up"))
            )
        };

        var samples = RunAndSample(bt, 600, sampleEvery: 30, arenaH: 330f);
        float groundY = samples[0].pos.Y;

        // After 3 seconds (tick 180+), fighter should be above ground
        int aboveGroundCount = 0;
        int totalLate = 0;
        foreach (var (tick, pos) in samples)
        {
            if (tick < 180) continue;
            totalLate++;
            if (pos.Y < groundY - 30f) aboveGroundCount++;
        }

        Assert(totalLate > 0, "Not enough late samples");
        float pct = (float)aboveGroundCount / totalLate;
        Assert(pct > 0.3f, $"Expected fighter to be airborne >30% of time after 3s, was {pct * 100:F0}%");
    }

    /// <summary>
    /// Control test: no BT actions, fighter should fall and stay on ground.
    /// Validates that gravity works and grounding is stable.
    /// </summary>
    private void TestGravityFall()
    {
        // Spawn mid-air, no actions
        var arena = new Arena(1000f, 600f);
        var startPos = new Vector2(500f, 100f); // high up
        var fighter = new Fighter(0, startPos);
        var dummy = new Fighter(1, new Vector2(900f, arena.Bounds.End.Y - 20f));
        var bt = new BehaviorTreeRunner(new List<BtNode> { Cond(Var.Never) }); // no-op
        var dummyBt = new BehaviorTreeRunner(new List<BtNode> { Cond(Var.Never) });

        float groundY = arena.Bounds.End.Y - fighter.BodyRadius;
        bool hitGround = false;
        int groundTick = -1;

        for (int t = 0; t < 300; t++)
        {
            var ctx = new FighterBtContext(fighter, dummy, arena, t);
            bt.Apply(ctx);
            TickFighter(fighter, arena);

            if (!hitGround && fighter.IsGrounded)
            {
                hitGround = true;
                groundTick = t;
            }
        }

        Assert(hitGround, "Fighter never hit the ground under gravity");
        Assert(groundTick < 120, $"Took too long to hit ground: {groundTick} ticks");

        // After landing, should stay grounded and not drift
        float finalX = fighter.Position.X;
        float startX = startPos.X;
        Assert(Mathf.Abs(finalX - startX) < 5f, $"Fighter drifted horizontally: start={startX:F0} end={finalX:F0}");
        Assert(fighter.IsGrounded, "Fighter should be grounded at end");
    }
}
