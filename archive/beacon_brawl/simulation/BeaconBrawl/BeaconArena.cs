// ─────────────────────────────────────────────────────────────────────────────
// BeaconArena.cs — Arena with floating platform, beacon zones, and base zones
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation.BeaconBrawl;

public class BeaconArena
{
    public float Width { get; }
    public float Height { get; }
    public Rect2 Bounds { get; }
    public float WallThickness { get; } = 10f;

    /// <summary>Center platform: solid rectangle pawns can stand on.</summary>
    public Rect2 Platform { get; }

    /// <summary>Three beacon zones (left, center, right).</summary>
    public BeaconZone[] BeaconZones { get; }

    /// <summary>Base zones for each team (index 0 = Team A, 1 = Team B).</summary>
    public BaseZone[] BaseZones { get; }

    /// <summary>Spawn positions per team (index 0 = Team A, 1 = Team B).</summary>
    public Vector2[] SpawnPositions { get; }

    public BeaconArena(float width = 2000f, float height = 800f)
    {
        Width = width;
        Height = height;
        Bounds = new Rect2(WallThickness, WallThickness,
            Width - WallThickness * 2, Height - WallThickness * 2);

        // Center platform: 250×20, centered horizontally, at y=620
        // Lowered from y=380 to be reachable via jump (~90px) + hook (280px)
        float platW = 250f, platH = 20f;
        float platX = width / 2f - platW / 2f;
        float platY = 620f;
        Platform = new Rect2(platX, platY, platW, platH);

        // Beacon zones (spread wider for new map)
        float groundY = Bounds.End.Y - 16f; // ground level for pawn radius=16
        BeaconZones =
        [
            new BeaconZone(0, new Vector2(500f, groundY), 80f, 1),            // Left (ground)
            new BeaconZone(1, new Vector2(width / 2f, platY - 10f), 80f, 1),  // Center (on platform, 2x multiplier on side beacons)
            new BeaconZone(2, new Vector2(1500f, groundY), 80f, 1),           // Right (ground)
        ];

        // Base zones: rear corners behind spawn points
        BaseZones =
        [
            new BaseZone(0, new Vector2(100f, groundY), 100f),   // Team A base (far left)
            new BaseZone(1, new Vector2(1900f, groundY), 100f),  // Team B base (far right)
        ];

        // Spawn positions (same as base zone centers)
        SpawnPositions =
        [
            new Vector2(100f, groundY),   // Team A spawn
            new Vector2(1900f, groundY),  // Team B spawn
        ];
    }

    /// <summary>Clamp position inside arena and zero out velocity on contact.</summary>
    public void ClampToArena(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        float left = Bounds.Position.X + radius;
        float right = Bounds.End.X - radius;
        float top = Bounds.Position.Y + radius;
        float bottom = Bounds.End.Y - radius;

        if (pos.X < left) { pos = new Vector2(left, pos.Y); vel = new Vector2(Mathf.Max(0, vel.X), vel.Y); }
        if (pos.X > right) { pos = new Vector2(right, pos.Y); vel = new Vector2(Mathf.Min(0, vel.X), vel.Y); }
        if (pos.Y < top) { pos = new Vector2(pos.X, top); vel = new Vector2(vel.X, Mathf.Max(0, vel.Y)); }
        if (pos.Y > bottom) { pos = new Vector2(pos.X, bottom); vel = new Vector2(vel.X, Mathf.Min(0, vel.Y)); }
    }

    /// <summary>Check if position is on the ground.</summary>
    public bool IsOnGround(Vector2 pos, float radius)
    {
        return pos.Y >= Bounds.End.Y - radius - 1f;
    }

    /// <summary>Check if position is standing on the center platform.</summary>
    public bool IsOnPlatform(Vector2 pos, float radius)
    {
        float platTop = Platform.Position.Y;
        bool onTop = pos.Y >= platTop - radius - 1f && pos.Y <= platTop - radius + 4f;
        bool withinX = pos.X >= Platform.Position.X - radius && pos.X <= Platform.End.X + radius;
        return onTop && withinX;
    }

    /// <summary>
    /// Resolve platform collision: land on top, deflect from bottom in center third only.
    /// Outer 1/3 on each side is one-sided (jump-through from below, land on top).
    /// </summary>
    public void ResolvePlatformCollision(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        float platTop = Platform.Position.Y;
        float platBottom = Platform.End.Y;
        bool withinX = pos.X >= Platform.Position.X - radius && pos.X <= Platform.End.X + radius;

        if (!withinX) return;

        float feetY = pos.Y + radius;
        float headY = pos.Y - radius;

        // Landing on top (one-sided: always works — you can land from above everywhere)
        if (vel.Y >= 0 && feetY >= platTop && feetY <= platTop + 8f && headY <= platTop)
        {
            pos = new Vector2(pos.X, platTop - radius);
            vel = new Vector2(vel.X, Mathf.Min(0, vel.Y));
        }
        // Rising through bottom — only block in center 1/3 (solid underside)
        else if (vel.Y < 0 && headY >= platTop && headY <= platBottom && feetY > platBottom)
        {
            float third = Platform.Size.X / 3f;
            float solidLeft = Platform.Position.X + third;
            float solidRight = Platform.End.X - third;
            if (pos.X >= solidLeft && pos.X <= solidRight)
            {
                pos = new Vector2(pos.X, platBottom + radius);
                vel = new Vector2(vel.X, Mathf.Max(0, vel.Y));
            }
            // else: one-sided edge — pass through from below
        }
    }

    /// <summary>Check if a ray segment from origin in direction hits any wall. Returns hit info.</summary>
    public bool RaycastWall(Vector2 origin, Vector2 direction, float maxDist,
        out Vector2 hitPoint, out Vector2 hitNormal, out float hitDist)
    {
        hitPoint = origin;
        hitNormal = Vector2.Zero;
        hitDist = maxDist;
        bool hit = false;

        float left = Bounds.Position.X;
        float right = Bounds.End.X;
        float top = Bounds.Position.Y;
        float bottom = Bounds.End.Y;

        // Check all 4 walls + platform surfaces
        // Left wall
        if (TryRayWall(origin, direction, maxDist, left, true, top, bottom, new Vector2(1, 0),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Right wall
        if (TryRayWall(origin, direction, maxDist, right, true, top, bottom, new Vector2(-1, 0),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Top wall
        if (TryRayWall(origin, direction, maxDist, top, false, left, right, new Vector2(0, 1),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Bottom wall (floor)
        if (TryRayWall(origin, direction, maxDist, bottom, false, left, right, new Vector2(0, -1),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;

        // Platform top
        float platTop = Platform.Position.Y;
        float platLeft = Platform.Position.X;
        float platRight = Platform.End.X;
        if (TryRayWall(origin, direction, maxDist, platTop, false, platLeft, platRight, new Vector2(0, -1),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Platform bottom
        float platBottom = Platform.End.Y;
        if (TryRayWall(origin, direction, maxDist, platBottom, false, platLeft, platRight, new Vector2(0, 1),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Platform left side
        if (TryRayWall(origin, direction, maxDist, platLeft, true, platTop, platBottom, new Vector2(-1, 0),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;
        // Platform right side
        if (TryRayWall(origin, direction, maxDist, platRight, true, platTop, platBottom, new Vector2(1, 0),
                ref hitPoint, ref hitNormal, ref hitDist)) hit = true;

        return hit;
    }

    private static bool TryRayWall(Vector2 origin, Vector2 dir, float maxDist,
        float wallPos, bool isVertical, float min, float max, Vector2 normal,
        ref Vector2 bestHitPoint, ref Vector2 bestHitNormal, ref float bestDist)
    {
        // For vertical wall: solve origin.X + dir.X * t = wallPos
        // For horizontal wall: solve origin.Y + dir.Y * t = wallPos
        float component = isVertical ? dir.X : dir.Y;
        float originComp = isVertical ? origin.X : origin.Y;

        if (Mathf.Abs(component) < 0.001f) return false;

        float t = (wallPos - originComp) / component;
        if (t < 0 || t > maxDist) return false;

        Vector2 point = origin + dir * t;
        float cross = isVertical ? point.Y : point.X;
        if (cross < min || cross > max) return false;

        if (t < bestDist)
        {
            bestDist = t;
            bestHitPoint = point;
            bestHitNormal = normal;
            return true;
        }
        return false;
    }
}

/// <summary>Definition of a beacon capture zone.</summary>
public record BeaconZone(int Index, Vector2 Center, float Radius, int PointMultiplier);

/// <summary>Definition of a team base zone (fast regen area).</summary>
public record BaseZone(int TeamIndex, Vector2 Center, float Radius)
{
    public bool Contains(Vector2 pos) => pos.DistanceTo(Center) <= Radius;
}
