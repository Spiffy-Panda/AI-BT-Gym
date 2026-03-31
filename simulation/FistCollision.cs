// ─────────────────────────────────────────────────────────────────────────────
// FistCollision.cs — Fist-vs-surface collision subsystem (partial Match)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation;

public partial class Match
{
    // ── Fist-vs-surface collision system ──
    // Extending fists auto-lock when they hit: arena walls/ceiling, platforms,
    // destructible walls (damage + attach), shaped ceiling, shrink bounds.

    private void TickFistSurfaces(Fighter f)
    {
        CheckFistSurface(f, f.LeftFist);
        CheckFistSurface(f, f.RightFist);
    }

    private void CheckFistSurface(Fighter owner, Fist fist)
    {
        if (fist.ChainState != FistChainState.Extending) return;

        var pos = fist.Position;
        float r = fist.FistRadius;

        // 1. Arena walls and ceiling
        if (CheckFistVsArenaWalls(fist, pos, r)) return;

        // 2. Shaped ceiling
        if (CheckFistVsCeiling(fist, pos, r)) return;

        // 3. Platforms (attach to surface)
        if (CheckFistVsPlatforms(fist, pos, r)) return;

        // 4. Destructible walls (damage + attach or retract)
        if (CheckFistVsDestructibleWalls(fist, pos, r)) return;

        // 5. Shrink bounds
        if (CheckFistVsShrinkBounds(fist, pos, r)) return;
    }

    private bool CheckFistVsArenaWalls(Fist fist, Vector2 pos, float r)
    {
        var b = Arena.Bounds;

        // Left wall
        if (pos.X - r <= b.Position.X)
        {
            fist.Position = new Vector2(b.Position.X + r, pos.Y);
            fist.AnchorPoint = fist.Position;
            AutoLockFist(fist);
            return true;
        }
        // Right wall
        if (pos.X + r >= b.End.X)
        {
            fist.Position = new Vector2(b.End.X - r, pos.Y);
            fist.AnchorPoint = fist.Position;
            AutoLockFist(fist);
            return true;
        }
        // Floor
        if (pos.Y + r >= b.End.Y)
        {
            fist.Position = new Vector2(pos.X, b.End.Y - r);
            fist.AnchorPoint = fist.Position;
            AutoLockFist(fist);
            return true;
        }
        // Flat ceiling (only when no shaped ceiling — shaped handled separately)
        if (Arena.Config.Ceiling == null && pos.Y - r <= b.Position.Y)
        {
            fist.Position = new Vector2(pos.X, b.Position.Y + r);
            fist.AnchorPoint = fist.Position;
            AutoLockFist(fist);
            return true;
        }
        return false;
    }

    private bool CheckFistVsCeiling(Fist fist, Vector2 pos, float r)
    {
        if (Arena.Config.Ceiling == null) return false;

        float ceilY = Arena.GetCeilingY(pos.X);
        if (pos.Y - r <= ceilY)
        {
            fist.Position = new Vector2(pos.X, ceilY + r);
            fist.AnchorPoint = fist.Position;
            AutoLockFist(fist);
            return true;
        }
        return false;
    }

    private bool CheckFistVsPlatforms(Fist fist, Vector2 pos, float r)
    {
        foreach (var plat in Arena.Config.Platforms)
        {
            float platLeft = plat.X - plat.Width / 2f;
            float platRight = plat.X + plat.Width / 2f;
            float platTop = plat.Y;
            float platBottom = plat.Y + plat.Height;

            // Check AABB overlap
            if (pos.X + r > platLeft && pos.X - r < platRight &&
                pos.Y + r > platTop && pos.Y - r < platBottom)
            {
                // Snap to nearest surface
                float dTop = Mathf.Abs(pos.Y - r - platTop);
                float dBot = Mathf.Abs(pos.Y + r - platBottom);
                float dLeft = Mathf.Abs(pos.X + r - platLeft);
                float dRight = Mathf.Abs(pos.X - r - platRight);
                float min = Mathf.Min(Mathf.Min(dTop, dBot), Mathf.Min(dLeft, dRight));

                if (min == dBot)       // attach to underside
                    fist.Position = new Vector2(pos.X, platBottom + r);
                else if (min == dTop)  // attach to top
                    fist.Position = new Vector2(pos.X, platTop - r);
                else if (min == dLeft) // attach to left side
                    fist.Position = new Vector2(platLeft - r, pos.Y);
                else                   // attach to right side
                    fist.Position = new Vector2(platRight + r, pos.Y);

                fist.AnchorPoint = fist.Position;
                AutoLockFist(fist);
                return true;
            }
        }
        return false;
    }

    private bool CheckFistVsDestructibleWalls(Fist fist, Vector2 pos, float r)
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

            if (pos.X + r > wallLeft && pos.X - r < wallRight &&
                pos.Y + r > wallTop && pos.Y - r < wallBottom)
            {
                // Always deal damage and retract — destructible walls are punched, not grappled
                DestructibleWallHp[i] -= wall.DamagePerHit;
                if (DestructibleWallHp[i] <= 0)
                {
                    DestructibleWallHp[i] = 0;
                    DestructibleWallExists[i] = false;
                    ApplyWallBreakKnockback(wall);
                }
                fist.ForceRetract();
                return true;
            }
        }
        return false;
    }

    private bool CheckFistVsShrinkBounds(Fist fist, Vector2 pos, float r)
    {
        if (Arena.Config.Shrink == null) return false;

        if (pos.X - r <= EffectiveLeft)
        {
            fist.ForceRetract();
            return true;
        }
        if (pos.X + r >= EffectiveRight)
        {
            fist.ForceRetract();
            return true;
        }
        return false;
    }

    private void ApplyWallBreakKnockback(DestructibleWallConfig wall)
    {
        float knockRadius = wall.Height;
        float knockForce = 300f;

        foreach (var f in new[] { Fighter0, Fighter1 })
        {
            float dx = f.Position.X - wall.X;
            float dy = f.Position.Y - (wall.BottomY - wall.Height / 2f);
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < knockRadius && dist > 1f)
            {
                var dir = new Vector2(dx, dy) / dist;
                f.Velocity += dir * knockForce;
            }
        }
    }

    /// <summary>Auto-lock an extending fist onto a surface it just hit.</summary>
    private void AutoLockFist(Fist fist)
    {
        fist.AnchorPoint = fist.Position;
        if (fist.Lock())
        {
            _autoLockedThisTick.Add(fist);
        }
        else
        {
            fist.ForceRetract();
        }
    }
}
