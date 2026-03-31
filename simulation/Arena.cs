// ─────────────────────────────────────────────────────────────────────────────
// Arena.cs — Arena geometry and boundary enforcement
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Godot;

namespace AiBtGym.Simulation;

public class Arena
{
    public float Width { get; }
    public float Height { get; }
    public Rect2 Bounds { get; }
    public float WallThickness { get; } = 10f;
    public ArenaConfig Config { get; }

    public Arena() : this((ArenaConfig?)null) { }

    public Arena(ArenaConfig? config)
    {
        Config = config ?? new ArenaConfig();
        Width = Config.Width;
        Height = Config.Height;
        Bounds = new Rect2(WallThickness, WallThickness, Width - WallThickness * 2, Height - WallThickness * 2);
    }

    /// <summary>Legacy constructor for backwards compatibility.</summary>
    public Arena(float width, float height)
        : this(new ArenaConfig { Width = width, Height = height }) { }

    // ── Boundary enforcement ──

    /// <summary>Clamp position inside arena and zero out velocity on contact.</summary>
    public void ClampToArena(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        float left = Bounds.Position.X + radius;
        float right = Bounds.End.X - radius;
        float top = Bounds.Position.Y + radius;
        float bottom = Bounds.End.Y - radius;

        // Ceiling shape: use position-dependent ceiling Y if configured
        if (Config.Ceiling is CeilingConfig ceil)
        {
            // GetCeilingY returns absolute Y; add radius for body clearance
            float ceilY = GetCeilingY(pos.X) + radius;
            if (pos.Y < ceilY) { pos = new Vector2(pos.X, ceilY); vel = new Vector2(vel.X, Mathf.Max(0, vel.Y)); }
        }
        else
        {
            if (pos.Y < top) { pos = new Vector2(pos.X, top); vel = new Vector2(vel.X, Mathf.Max(0, vel.Y)); }
        }

        if (pos.X < left) { pos = new Vector2(left, pos.Y); vel = new Vector2(Mathf.Max(0, vel.X), vel.Y); }
        if (pos.X > right) { pos = new Vector2(right, pos.Y); vel = new Vector2(Mathf.Min(0, vel.X), vel.Y); }
        if (pos.Y > bottom) { pos = new Vector2(pos.X, bottom); vel = new Vector2(vel.X, Mathf.Min(0, vel.Y)); }

        // Corner bumpers: reflect velocity off diagonal surfaces
        foreach (var bumper in Config.CornerBumpers)
            ApplyCornerBumper(bumper, ref pos, ref vel, radius);
    }

    /// <summary>Clamp position inside effective bounds (accounts for arena shrink).</summary>
    public void ClampToEffectiveBounds(ref Vector2 pos, ref Vector2 vel, float radius,
        float effectiveLeft, float effectiveRight)
    {
        if (pos.X < effectiveLeft + radius)
        {
            pos = new Vector2(effectiveLeft + radius, pos.Y);
            vel = new Vector2(Mathf.Max(0, vel.X), vel.Y);
        }
        if (pos.X > effectiveRight - radius)
        {
            pos = new Vector2(effectiveRight - radius, pos.Y);
            vel = new Vector2(Mathf.Min(0, vel.X), vel.Y);
        }
    }

    // ── Ground checks ──

    /// <summary>Check if position is on the arena floor.</summary>
    public bool IsOnGround(Vector2 pos, float radius)
    {
        return pos.Y >= Bounds.End.Y - radius - 1f;
    }

    /// <summary>Check if position is on any platform surface.</summary>
    public bool IsOnPlatform(Vector2 pos, float radius, Vector2 vel)
    {
        foreach (var plat in Config.Platforms)
        {
            if (IsOnPlatformSurface(plat, pos, radius, vel))
                return true;
        }
        return false;
    }

    /// <summary>Check if position is on a specific platform surface.</summary>
    public bool IsOnPlatformSurface(PlatformConfig plat, Vector2 pos, float radius, Vector2 vel)
    {
        float platLeft = plat.X - plat.Width / 2f;
        float platRight = plat.X + plat.Width / 2f;
        float platTop = plat.Y;

        // Must be horizontally within platform bounds
        if (pos.X < platLeft || pos.X > platRight) return false;

        // Feet must be at platform surface level
        float feetY = pos.Y + radius;
        if (feetY < platTop - 1f || feetY > platTop + plat.Height) return false;

        // One-way platforms: only support from above (vel.Y >= 0 means falling or stationary)
        if (plat.OneWay && vel.Y < -1f) return false;

        return true;
    }

    /// <summary>Apply platform collision: prevent falling through platforms.</summary>
    public void ClampToPlatforms(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        foreach (var plat in Config.Platforms)
        {
            float platLeft = plat.X - plat.Width / 2f;
            float platRight = plat.X + plat.Width / 2f;
            float platTop = plat.Y;

            // Must be horizontally within platform
            if (pos.X < platLeft || pos.X > platRight) continue;

            float feetY = pos.Y + radius;

            // One-way: only collide when falling onto the top surface
            if (plat.OneWay)
            {
                if (vel.Y > 0 && feetY >= platTop && feetY <= platTop + plat.Height + vel.Y * SimPhysics.FixedDt)
                {
                    pos = new Vector2(pos.X, platTop - radius);
                    vel = new Vector2(vel.X, Mathf.Min(0, vel.Y));
                }
            }
            else
            {
                // Solid platform: collide from all sides
                float platBottom = platTop + plat.Height;
                float headY = pos.Y - radius;
                float bodyLeft = pos.X - radius;
                float bodyRight = pos.X + radius;

                // Check if body overlaps the platform AABB
                bool overlapX = bodyRight > platLeft && bodyLeft < platRight;
                bool overlapY = (pos.Y + radius) > platTop && (pos.Y - radius) < platBottom;

                if (overlapX && overlapY)
                {
                    // Find minimum penetration axis to push out
                    float pushUp = (pos.Y + radius) - platTop;     // push up to land on top
                    float pushDown = platBottom - (pos.Y - radius); // push down from bottom
                    float pushLeftAmt = bodyRight - platLeft;       // push left
                    float pushRightAmt = platRight - bodyLeft;      // push right

                    float minPush = Mathf.Min(Mathf.Min(pushUp, pushDown), Mathf.Min(pushLeftAmt, pushRightAmt));

                    if (minPush == pushUp)
                    {
                        pos = new Vector2(pos.X, platTop - radius);
                        vel = new Vector2(vel.X, Mathf.Min(0, vel.Y));
                    }
                    else if (minPush == pushDown)
                    {
                        pos = new Vector2(pos.X, platBottom + radius);
                        vel = new Vector2(vel.X, Mathf.Max(0, vel.Y));
                    }
                    else if (minPush == pushLeftAmt)
                    {
                        pos = new Vector2(platLeft - radius, pos.Y);
                        vel = new Vector2(Mathf.Min(0, vel.X), vel.Y);
                    }
                    else // pushRightAmt
                    {
                        pos = new Vector2(platRight + radius, pos.Y);
                        vel = new Vector2(Mathf.Max(0, vel.X), vel.Y);
                    }
                }
            }
        }
    }

    // ── Hazard zones ──

    /// <summary>Check if a grounded position is in any hazard zone.</summary>
    public bool IsInHazardZone(Vector2 pos)
    {
        foreach (var hz in Config.HazardZones)
        {
            if (pos.X >= hz.X && pos.X <= hz.X + hz.Width)
                return true;
        }
        return false;
    }

    /// <summary>Get the damage rate for the hazard zone at this position (0 if none).</summary>
    public float GetHazardDamageRate(Vector2 pos)
    {
        foreach (var hz in Config.HazardZones)
        {
            if (pos.X >= hz.X && pos.X <= hz.X + hz.Width)
                return hz.DamagePerSecond;
        }
        return 0f;
    }

    // ── Wall friction zones ──

    /// <summary>Check if position is in a wall friction zone and return the multiplier.</summary>
    public float GetWallFrictionMultiplier(Vector2 pos, float radius)
    {
        float leftWallX = Bounds.Position.X + radius;
        float rightWallX = Bounds.End.X - radius;

        foreach (var wfz in Config.WallFrictionZones)
        {
            bool touchingWall = false;
            if (wfz.Side == "left" && Mathf.Abs(pos.X - leftWallX) < 5f) touchingWall = true;
            if (wfz.Side == "right" && Mathf.Abs(pos.X - rightWallX) < 5f) touchingWall = true;

            if (touchingWall && pos.Y >= wfz.TopY && pos.Y <= wfz.BottomY)
                return wfz.FrictionMultiplier;
        }
        return 1f; // no friction
    }

    /// <summary>Check if position is in any wall friction zone.</summary>
    public bool IsInWallFrictionZone(Vector2 pos, float radius)
    {
        return GetWallFrictionMultiplier(pos, radius) < 1f;
    }

    // ── Ceiling ──

    /// <summary>Get the ceiling Y at a given X position (accounts for ceiling shape).</summary>
    public float GetCeilingY(float x)
    {
        if (Config.Ceiling is not CeilingConfig ceil)
            return Bounds.Position.Y;

        // Linear interpolation: edge at edges, center at center X
        float centerX = Width / 2f;
        float t = 1f - Mathf.Abs(x - centerX) / centerX;
        return Mathf.Lerp(ceil.EdgeY, ceil.CenterY, t);
    }

    // ── Corner bumpers ──

    private void ApplyCornerBumper(CornerBumperConfig bumper, ref Vector2 pos, ref Vector2 vel, float radius)
    {
        float size = bumper.Size;
        Vector2 cornerPos;
        Vector2 normal; // diagonal surface normal pointing into the arena

        switch (bumper.Corner)
        {
            case "top_left":
                cornerPos = Bounds.Position;
                normal = new Vector2(1, 1).Normalized();
                break;
            case "top_right":
                cornerPos = new Vector2(Bounds.End.X, Bounds.Position.Y);
                normal = new Vector2(-1, 1).Normalized();
                break;
            case "bottom_left":
                cornerPos = new Vector2(Bounds.Position.X, Bounds.End.Y);
                normal = new Vector2(1, -1).Normalized();
                break;
            case "bottom_right":
                cornerPos = Bounds.End;
                normal = new Vector2(-1, -1).Normalized();
                break;
            default:
                return;
        }

        // Check if fighter is within the bumper triangle
        Vector2 toPos = pos - cornerPos;
        // The bumper is a triangle: the diagonal line goes from (cornerX, cornerY ± size) to (cornerX ± size, cornerY)
        // We check if the fighter is on the wrong side of the diagonal
        float distAlongNormal = toPos.Dot(normal);

        // Only apply if within the bumper region (close to corner) and penetrating the diagonal
        float distFromCorner = toPos.Length();
        if (distFromCorner < size + radius && distAlongNormal < radius)
        {
            // Push out along the normal
            pos += normal * (radius - distAlongNormal);

            // Reflect velocity off the diagonal
            float velAlongNormal = vel.Dot(normal);
            if (velAlongNormal < 0) // moving into the bumper
                vel -= 2f * velAlongNormal * normal;
        }
    }

    // ── Nearest platform queries (for BT context) ──

    /// <summary>Find the nearest platform to a position. Returns null if no platforms.</summary>
    public PlatformConfig? GetNearestPlatform(Vector2 pos)
    {
        PlatformConfig? nearest = null;
        float bestDist = float.MaxValue;

        foreach (var plat in Config.Platforms)
        {
            float dx = pos.X - plat.X;
            float dy = pos.Y - plat.Y;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = plat;
            }
        }
        return nearest;
    }

    /// <summary>Distance from position to nearest platform edge.</summary>
    public float DistanceToNearestPlatform(Vector2 pos)
    {
        float best = float.MaxValue;
        foreach (var plat in Config.Platforms)
        {
            float platLeft = plat.X - plat.Width / 2f;
            float platRight = plat.X + plat.Width / 2f;

            // Closest X on platform surface
            float cx = Mathf.Clamp(pos.X, platLeft, platRight);
            float cy = plat.Y; // top surface
            float dist = pos.DistanceTo(new Vector2(cx, cy));
            if (dist < best) best = dist;
        }
        return best;
    }
}
