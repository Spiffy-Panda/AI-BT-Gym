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

    /// <summary>
    /// What each fighter's BT is told the map looks like. Null = full knowledge (sees real arena).
    /// This lets you test whether knowing about specific features improves a BT's performance.
    /// </summary>
    public ArenaConfig? KnownConfig0 { get; set; }
    public ArenaConfig? KnownConfig1 { get; set; }

    // ── Mutable feature state ──

    /// <summary>Current HP for each destructible wall (indexed same as Arena.Config.DestructibleWalls).</summary>
    public float[] DestructibleWallHp { get; private set; } = [];

    /// <summary>Whether each destructible wall still exists.</summary>
    public bool[] DestructibleWallExists { get; private set; } = [];

    /// <summary>Whether each pickup is currently active/available.</summary>
    public bool[] PickupActive { get; private set; } = [];

    /// <summary>Ticks remaining until each pickup respawns.</summary>
    public int[] PickupRespawnTimer { get; private set; } = [];

    /// <summary>Current effective left boundary (accounts for arena shrink).</summary>
    public float EffectiveLeft { get; private set; }

    /// <summary>Current effective right boundary (accounts for arena shrink).</summary>
    public float EffectiveRight { get; private set; }

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

            // Nudge spawn positions out of hazard zones
            posL = NudgeOutOfHazard(posL, arena);
            posR = NudgeOutOfHazard(posR, arena);

            // Randomize which fighter gets left vs right
            bool swap = rng.Next(2) == 1;
            Fighter0 = new Fighter(0, swap ? posR : posL);
            Fighter1 = new Fighter(1, swap ? posL : posR);
        }
        else
        {
            float spawnMargin = arena.Width * 0.25f;
            var posL = new Vector2(arena.Bounds.Position.X + spawnMargin, groundY);
            var posR = new Vector2(arena.Bounds.End.X - spawnMargin, groundY);

            posL = NudgeOutOfHazard(posL, arena);
            posR = NudgeOutOfHazard(posR, arena);

            Fighter0 = new Fighter(0, posL);
            Fighter1 = new Fighter(1, posR);
        }

        _bt0 = new BehaviorTreeRunner(tree0);
        _bt1 = new BehaviorTreeRunner(tree1);

        InitFeatureState();
    }

    private void InitFeatureState()
    {
        var config = Arena.Config;

        // Destructible walls
        int wallCount = config.DestructibleWalls.Count;
        DestructibleWallHp = new float[wallCount];
        DestructibleWallExists = new bool[wallCount];
        for (int i = 0; i < wallCount; i++)
        {
            DestructibleWallHp[i] = config.DestructibleWalls[i].Hp;
            DestructibleWallExists[i] = true;
        }

        // Pickups: start active
        int pickupCount = config.Pickups.Count;
        PickupActive = new bool[pickupCount];
        PickupRespawnTimer = new int[pickupCount];
        for (int i = 0; i < pickupCount; i++)
            PickupActive[i] = true;

        // Effective bounds
        EffectiveLeft = Arena.Bounds.Position.X;
        EffectiveRight = Arena.Bounds.End.X;
    }

    /// <summary>Advance the match by one simulation tick.</summary>
    public void Step()
    {
        if (IsOver) return;

        // Run behavior trees — each fighter sees its own known config (or the real arena if null)
        var ctx0 = new FighterBtContext(Fighter0, Fighter1, Arena, Tick, this, KnownConfig0);
        var ctx1 = new FighterBtContext(Fighter1, Fighter0, Arena, Tick, this, KnownConfig1);

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

        // Fist-vs-destructible-wall collisions
        CheckFistVsWalls(Fighter0);
        CheckFistVsWalls(Fighter1);

        // Fist-vs-body hits (notify recorder on hit)
        CheckAndRecordHit(Fighter0.LeftFist, Fighter1, 0, "left");
        CheckAndRecordHit(Fighter0.RightFist, Fighter1, 0, "right");
        CheckAndRecordHit(Fighter1.LeftFist, Fighter0, 1, "left");
        CheckAndRecordHit(Fighter1.RightFist, Fighter0, 1, "right");

        // Arena features: hazards, pickups, shrink
        TickHazardZones(Fighter0);
        TickHazardZones(Fighter1);
        TickPickups(Fighter0);
        TickPickups(Fighter1);
        TickArenaShrink();

        // Let recorder sample per-tick state
        Recorder?.RecordTick(this);

        Tick++;
        CheckWinConditions();
    }

    private void CheckAndRecordHit(Fist fist, Fighter target, int attackerIdx, string hand)
    {
        // Fists can't hit through standing destructible walls
        if (IsBlockedByWall(fist.Position, target.Position))
            return;

        if (SimPhysics.CheckFistHitBody(fist, target, out float damage))
        {
            Recorder?.RecordHit(Tick, attackerIdx, hand, damage,
                Fighter0.Position, Fighter1.Position);
        }
    }

    /// <summary>Check if a line between two points is blocked by any standing destructible wall.</summary>
    private bool IsBlockedByWall(Vector2 from, Vector2 to)
    {
        var walls = Arena.Config.DestructibleWalls;
        for (int i = 0; i < walls.Count; i++)
        {
            if (!DestructibleWallExists[i]) continue;
            var wall = walls[i];

            float wallLeft = wall.X - wall.Thickness / 2f;
            float wallRight = wall.X + wall.Thickness / 2f;
            float wallTop = wall.BottomY - wall.Height;
            float wallBottom = wall.BottomY;

            // Check if the line from→to crosses the wall vertically
            // Both points on the same side of the wall = not blocked
            if (from.X < wallLeft && to.X < wallLeft) continue;
            if (from.X > wallRight && to.X > wallRight) continue;

            // Line crosses the wall X range — check Y overlap
            // Interpolate Y at the wall X to see if it's within wall height
            float t = (wall.X - from.X) / (to.X - from.X + 0.001f);
            if (t < 0 || t > 1) continue;
            float yAtWall = from.Y + t * (to.Y - from.Y);

            if (yAtWall >= wallTop && yAtWall <= wallBottom)
                return true;
        }
        return false;
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

        // Platform collision
        Arena.ClampToPlatforms(ref pos, ref vel, f.BodyRadius);

        // Destructible wall collision (fighter body vs wall)
        ClampToDestructibleWalls(ref pos, ref vel, f.BodyRadius);

        // Arena shrink bounds
        if (Arena.Config.Shrink != null)
            Arena.ClampToEffectiveBounds(ref pos, ref vel, f.BodyRadius, EffectiveLeft, EffectiveRight);

        f.Position = pos;
        f.Velocity = vel;

        // Ground check: floor OR platform
        f.IsGrounded = Arena.IsOnGround(f.Position, f.BodyRadius)
            || Arena.IsOnPlatform(f.Position, f.BodyRadius, f.Velocity);

        // Override grounding when being pulled by a grapple — prevents ground friction fighting the pull
        if ((f.LeftFist.IsAttachedToWorld && f.LeftFist.ChainState == FistChainState.Retracting) ||
            (f.RightFist.IsAttachedToWorld && f.RightFist.ChainState == FistChainState.Retracting))
            f.IsGrounded = false;

        // Wall friction zones: dampen Y velocity when sliding on upper walls
        float frictionMult = Arena.GetWallFrictionMultiplier(f.Position, f.BodyRadius);
        if (frictionMult < 1f && f.Velocity.Y > 0) // only slow descent, not ascent
            f.Velocity = new Vector2(f.Velocity.X, f.Velocity.Y * frictionMult);

        // Tick fists
        f.LeftFist.Tick(SimPhysics.FixedDt, f.Position);
        f.RightFist.Tick(SimPhysics.FixedDt, f.Position);
    }

    // ── Destructible walls ──

    private void ClampToDestructibleWalls(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        var walls = Arena.Config.DestructibleWalls;
        for (int i = 0; i < walls.Count; i++)
        {
            if (!DestructibleWallExists[i]) continue;
            var wall = walls[i];

            float wallLeft = wall.X - wall.Thickness / 2f;
            float wallRight = wall.X + wall.Thickness / 2f;
            float wallTop = wall.BottomY - wall.Height;
            float wallBottom = wall.BottomY;

            // Check if fighter body overlaps the wall
            if (pos.Y + radius > wallTop && pos.Y - radius < wallBottom)
            {
                if (pos.X + radius > wallLeft && pos.X - radius < wallRight)
                {
                    // Push out to nearest side
                    float pushLeft = wallLeft - radius - pos.X;
                    float pushRight = wallRight + radius - pos.X;

                    if (Mathf.Abs(pushLeft) < Mathf.Abs(pushRight))
                    {
                        pos = new Vector2(wallLeft - radius, pos.Y);
                        vel = new Vector2(Mathf.Min(0, vel.X), vel.Y);
                    }
                    else
                    {
                        pos = new Vector2(wallRight + radius, pos.Y);
                        vel = new Vector2(Mathf.Max(0, vel.X), vel.Y);
                    }
                }
            }
        }
    }

    private void CheckFistVsWalls(Fighter f)
    {
        CheckFistVsWall(f.LeftFist);
        CheckFistVsWall(f.RightFist);
    }

    private void CheckFistVsWall(Fist fist)
    {
        if (fist.ChainState != FistChainState.Extending) return;

        var walls = Arena.Config.DestructibleWalls;
        for (int i = 0; i < walls.Count; i++)
        {
            if (!DestructibleWallExists[i]) continue;
            var wall = walls[i];

            float wallLeft = wall.X - wall.Thickness / 2f;
            float wallRight = wall.X + wall.Thickness / 2f;
            float wallTop = wall.BottomY - wall.Height;
            float wallBottom = wall.BottomY;

            // Check fist overlap with wall
            if (fist.Position.X + fist.FistRadius > wallLeft &&
                fist.Position.X - fist.FistRadius < wallRight &&
                fist.Position.Y + fist.FistRadius > wallTop &&
                fist.Position.Y - fist.FistRadius < wallBottom)
            {
                DestructibleWallHp[i] -= wall.DamagePerHit;
                if (DestructibleWallHp[i] <= 0)
                {
                    DestructibleWallHp[i] = 0;
                    DestructibleWallExists[i] = false;
                }
                fist.ForceRetract();
                return;
            }
        }
    }

    // ── Hazard zones ──

    private void TickHazardZones(Fighter f)
    {
        // Only apply hazard damage when on the arena floor, not elevated on platforms
        if (!Arena.IsOnGround(f.Position, f.BodyRadius)) return;
        float dmgRate = Arena.GetHazardDamageRate(f.Position);
        if (dmgRate > 0)
            f.ApplyDamage(dmgRate * SimPhysics.FixedDt);
    }

    // ── Pickups ──

    private void TickPickups(Fighter f)
    {
        var pickups = Arena.Config.Pickups;
        for (int i = 0; i < pickups.Count; i++)
        {
            if (!PickupActive[i])
            {
                // Tick respawn timer (only once per tick — use Fighter0 to avoid double-counting)
                if (f == Fighter0)
                {
                    PickupRespawnTimer[i]--;
                    if (PickupRespawnTimer[i] <= 0)
                        PickupActive[i] = true;
                }
                continue;
            }

            var pickup = pickups[i];
            float dist = f.Position.DistanceTo(new Vector2(pickup.X, pickup.Y));
            if (dist < f.BodyRadius + 12f) // 12px pickup radius
            {
                // Heal the fighter
                float healAmount = Mathf.Min(pickup.HealAmount, pickup.MaxHp - f.Health);
                if (healAmount > 0)
                    f.ApplyDamage(-healAmount); // negative damage = heal

                PickupActive[i] = false;
                PickupRespawnTimer[i] = (int)(pickup.RespawnSeconds * 60f); // convert to ticks
            }
        }
    }

    // ── Arena shrink ──

    private void TickArenaShrink()
    {
        if (Arena.Config.Shrink is not ArenaShrinkConfig shrink) return;

        int startTick = (int)(MaxTicks * shrink.StartFraction);
        if (Tick < startTick) return;

        int ticksSinceStart = Tick - startTick;
        int stepInterval = (int)(shrink.StepIntervalSeconds * 60f);
        if (stepInterval <= 0) return;

        int steps = ticksSinceStart / stepInterval;
        float totalShrink = steps * shrink.ShrinkPerStep;

        float boundsCenter = Arena.Bounds.Position.X + Arena.Bounds.Size.X / 2f;
        float halfMin = shrink.MinWidth / 2f;

        EffectiveLeft = Mathf.Min(Arena.Bounds.Position.X + totalShrink, boundsCenter - halfMin);
        EffectiveRight = Mathf.Max(Arena.Bounds.End.X - totalShrink, boundsCenter + halfMin);
    }

    /// <summary>Nudge a spawn position out of hazard zones and destructible walls.</summary>
    private static Vector2 NudgeOutOfHazard(Vector2 pos, Arena arena)
    {
        foreach (var hz in arena.Config.HazardZones)
        {
            float hzLeft = hz.X;
            float hzRight = hz.X + hz.Width;

            if (pos.X >= hzLeft && pos.X <= hzRight)
            {
                // Move to whichever edge is closer
                float distToLeft = pos.X - hzLeft;
                float distToRight = hzRight - pos.X;

                if (distToLeft <= distToRight)
                    pos = new Vector2(hzLeft - 20f, pos.Y);   // 20px clearance
                else
                    pos = new Vector2(hzRight + 20f, pos.Y);

                // Clamp back into arena bounds
                pos = new Vector2(
                    Mathf.Clamp(pos.X, arena.Bounds.Position.X + 20f, arena.Bounds.End.X - 20f),
                    pos.Y);
                break;
            }
        }

        // Also nudge away from destructible walls
        foreach (var wall in arena.Config.DestructibleWalls)
        {
            float wallLeft = wall.X - wall.Thickness / 2f - 20f;
            float wallRight = wall.X + wall.Thickness / 2f + 20f;

            if (pos.X >= wallLeft && pos.X <= wallRight)
            {
                float distToLeft = pos.X - wallLeft;
                float distToRight = wallRight - pos.X;

                if (distToLeft <= distToRight)
                    pos = new Vector2(wallLeft - 5f, pos.Y);
                else
                    pos = new Vector2(wallRight + 5f, pos.Y);

                pos = new Vector2(
                    Mathf.Clamp(pos.X, arena.Bounds.Position.X + 20f, arena.Bounds.End.X - 20f),
                    pos.Y);
                break;
            }
        }

        return pos;
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
