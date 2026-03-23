// ─────────────────────────────────────────────────────────────────────────────
// Match.cs — Orchestrates a fight between two BT-driven fighters
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation;

public class Match
{
    public Fighter Fighter0 { get; }
    public Fighter Fighter1 { get; }
    public Arena Arena { get; }
    public int Tick { get; private set; }
    public int MaxTicks { get; set; } = 60 * 60; // 60 seconds at 60fps
    public bool IsOver { get; private set; }
    public int WinnerIndex { get; private set; } = -1;

    /// <summary>Optional recorder for capturing match events (hits, actions, state).</summary>
    public MatchRecorder? Recorder { get; set; }

    private readonly BehaviorTreeRunner _bt0;
    private readonly BehaviorTreeRunner _bt1;

    /// <summary>Create a match with deterministic starting positions (legacy).</summary>
    public Match(Arena arena, List<BtNode> tree0, List<BtNode> tree1)
        : this(arena, tree0, tree1, seed: null) { }

    /// <summary>
    /// Create a match with optional randomized starting positions.
    /// When seed is provided, spawn X positions are randomized within the arena
    /// and which fighter gets left vs right is randomized.
    /// </summary>
    public Match(Arena arena, List<BtNode> tree0, List<BtNode> tree1, int? seed)
    {
        Arena = arena;
        float groundY = arena.Bounds.End.Y - 20f;

        if (seed is int s)
        {
            var rng = new Random(s);

            // Randomize spawn distance: 15%–35% from each edge
            float minMargin = arena.Width * 0.15f;
            float maxMargin = arena.Width * 0.35f;
            float marginL = minMargin + (float)rng.NextDouble() * (maxMargin - minMargin);
            float marginR = minMargin + (float)rng.NextDouble() * (maxMargin - minMargin);

            var posL = new Vector2(arena.Bounds.Position.X + marginL, groundY);
            var posR = new Vector2(arena.Bounds.End.X - marginR, groundY);

            // Randomize which fighter gets left vs right
            bool swap = rng.Next(2) == 1;
            Fighter0 = new Fighter(0, swap ? posR : posL);
            Fighter1 = new Fighter(1, swap ? posL : posR);
        }
        else
        {
            float spawnMargin = arena.Width * 0.25f;
            Fighter0 = new Fighter(0, new Vector2(arena.Bounds.Position.X + spawnMargin, groundY));
            Fighter1 = new Fighter(1, new Vector2(arena.Bounds.End.X - spawnMargin, groundY));
        }

        _bt0 = new BehaviorTreeRunner(tree0);
        _bt1 = new BehaviorTreeRunner(tree1);
    }

    /// <summary>Advance the match by one simulation tick.</summary>
    public void Step()
    {
        if (IsOver) return;

        // Run behavior trees
        var ctx0 = new FighterBtContext(Fighter0, Fighter1, Arena, Tick);
        var ctx1 = new FighterBtContext(Fighter1, Fighter0, Arena, Tick);

        // Wire up action recording if a recorder is attached
        if (Recorder != null)
        {
            ctx0.OnActionExecuted = action => Recorder.RecordAction(Tick, 0, action);
            ctx1.OnActionExecuted = action => Recorder.RecordAction(Tick, 1, action);
        }

        _bt0.Apply(ctx0);
        _bt1.Apply(ctx1);

        // Physics: integrate both fighters
        TickFighter(Fighter0);
        TickFighter(Fighter1);

        // Fist-vs-fist collisions (all four pairs)
        // Skipped when fighters are close ("inside the guard")
        float fighterDist = Fighter0.Position.DistanceTo(Fighter1.Position);
        float bodyRadius = Fighter0.BodyRadius;
        SimPhysics.CheckFistCollision(Fighter0.LeftFist, Fighter1.LeftFist, fighterDist, bodyRadius);
        SimPhysics.CheckFistCollision(Fighter0.LeftFist, Fighter1.RightFist, fighterDist, bodyRadius);
        SimPhysics.CheckFistCollision(Fighter0.RightFist, Fighter1.LeftFist, fighterDist, bodyRadius);
        SimPhysics.CheckFistCollision(Fighter0.RightFist, Fighter1.RightFist, fighterDist, bodyRadius);

        // Fist-vs-body hits (notify recorder on hit)
        CheckAndRecordHit(Fighter0.LeftFist, Fighter1, 0, "left");
        CheckAndRecordHit(Fighter0.RightFist, Fighter1, 0, "right");
        CheckAndRecordHit(Fighter1.LeftFist, Fighter0, 1, "left");
        CheckAndRecordHit(Fighter1.RightFist, Fighter0, 1, "right");

        // Let recorder sample per-tick state
        Recorder?.RecordTick(this);

        Tick++;
        CheckWinConditions();
    }

    private void CheckAndRecordHit(Fist fist, Fighter target, int attackerIdx, string hand)
    {
        if (SimPhysics.CheckFistHitBody(fist, target, out float damage))
        {
            Recorder?.RecordHit(Tick, attackerIdx, hand, damage,
                Fighter0.Position, Fighter1.Position);
        }
    }

    private void TickFighter(Fighter f)
    {
        var pos = f.Position;
        var vel = f.Velocity;

        // Gravity + friction + integration
        SimPhysics.Integrate(ref pos, ref vel, SimPhysics.FixedDt, f.IsGrounded);

        f.Position = pos;
        f.Velocity = vel;

        // Chain constraints (before arena clamp so grapple can override position)
        SimPhysics.ApplyChainConstraint(f, f.LeftFist, SimPhysics.FixedDt);
        SimPhysics.ApplyChainConstraint(f, f.RightFist, SimPhysics.FixedDt);

        // Arena bounds
        pos = f.Position;
        vel = f.Velocity;
        Arena.ClampToArena(ref pos, ref vel, f.BodyRadius);
        f.Position = pos;
        f.Velocity = vel;

        // Ground check
        f.IsGrounded = Arena.IsOnGround(f.Position, f.BodyRadius);

        // Override grounding when being pulled by a grapple — prevents ground friction fighting the pull
        if ((f.LeftFist.IsAttachedToWorld && f.LeftFist.ChainState == FistChainState.Retracting) ||
            (f.RightFist.IsAttachedToWorld && f.RightFist.ChainState == FistChainState.Retracting))
            f.IsGrounded = false;

        // Tick fists
        f.LeftFist.Tick(SimPhysics.FixedDt, f.Position);
        f.RightFist.Tick(SimPhysics.FixedDt, f.Position);
    }

    private void CheckWinConditions()
    {
        if (Fighter0.Health <= 0 && Fighter1.Health <= 0)
        {
            IsOver = true;
            WinnerIndex = -1; // draw
        }
        else if (Fighter0.Health <= 0)
        {
            IsOver = true;
            WinnerIndex = 1;
        }
        else if (Fighter1.Health <= 0)
        {
            IsOver = true;
            WinnerIndex = 0;
        }
        else if (Tick >= MaxTicks)
        {
            IsOver = true;
            WinnerIndex = Fighter0.Health > Fighter1.Health ? 0
                        : Fighter1.Health > Fighter0.Health ? 1
                        : -1;
        }
    }
}
