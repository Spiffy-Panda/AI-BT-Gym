// ─────────────────────────────────────────────────────────────────────────────
// Match.cs — Orchestrates a fight between two BT-driven fighters
// ─────────────────────────────────────────────────────────────────────────────

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

    private readonly BehaviorTreeRunner _bt0;
    private readonly BehaviorTreeRunner _bt1;

    public Match(Arena arena, List<BtNode> tree0, List<BtNode> tree1)
    {
        Arena = arena;
        float spawnMargin = arena.Width * 0.25f;
        float groundY = arena.Bounds.End.Y - 20f;

        Fighter0 = new Fighter(0, new Vector2(arena.Bounds.Position.X + spawnMargin, groundY));
        Fighter1 = new Fighter(1, new Vector2(arena.Bounds.End.X - spawnMargin, groundY));

        _bt0 = new BehaviorTreeRunner(tree0);
        _bt1 = new BehaviorTreeRunner(tree1);

        // Wire up surface check callbacks for all fists
        SetupSurfaceChecks(Fighter0);
        SetupSurfaceChecks(Fighter1);
    }

    private void SetupSurfaceChecks(Fighter f)
    {
        (bool, Vector2) Check(Vector2 pos) =>
            Arena.TryGetNearestSurface(pos, out var pt) ? (true, pt) : (false, Vector2.Zero);

        f.LeftFist.SurfaceCheck = Check;
        f.RightFist.SurfaceCheck = Check;
    }

    /// <summary>Advance the match by one simulation tick.</summary>
    public void Step()
    {
        if (IsOver) return;

        // Run behavior trees
        var ctx0 = new FighterBtContext(Fighter0, Fighter1, Arena, Tick);
        var ctx1 = new FighterBtContext(Fighter1, Fighter0, Arena, Tick);
        _bt0.Apply(ctx0);
        _bt1.Apply(ctx1);

        // Physics: integrate both fighters
        TickFighter(Fighter0);
        TickFighter(Fighter1);

        // Fist-vs-fist collisions (all four pairs)
        SimPhysics.CheckFistCollision(Fighter0.LeftFist, Fighter1.LeftFist);
        SimPhysics.CheckFistCollision(Fighter0.LeftFist, Fighter1.RightFist);
        SimPhysics.CheckFistCollision(Fighter0.RightFist, Fighter1.LeftFist);
        SimPhysics.CheckFistCollision(Fighter0.RightFist, Fighter1.RightFist);

        // Fist-vs-body hits
        SimPhysics.CheckFistHitBody(Fighter0.LeftFist, Fighter1, out _);
        SimPhysics.CheckFistHitBody(Fighter0.RightFist, Fighter1, out _);
        SimPhysics.CheckFistHitBody(Fighter1.LeftFist, Fighter0, out _);
        SimPhysics.CheckFistHitBody(Fighter1.RightFist, Fighter0, out _);

        Tick++;
        CheckWinConditions();
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

        // Tick fists — surface attach is checked inside Fist.Tick via SurfaceCheck callback
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
