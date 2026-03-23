// ─────────────────────────────────────────────────────────────────────────────
// Arena.cs — Arena geometry and surface queries
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation;

public class Arena
{
    public float Width { get; }
    public float Height { get; }
    public Rect2 Bounds { get; }
    public float WallThickness { get; } = 10f;

    // Surfaces that fists can attach to (all walls + ceiling)
    public float AttachRadius { get; } = 20f;

    public Arena(float width = 1200f, float height = 680f)
    {
        Width = width;
        Height = height;
        Bounds = new Rect2(WallThickness, WallThickness, Width - WallThickness * 2, Height - WallThickness * 2);
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

    /// <summary>
    /// Try to find the nearest wall/ceiling surface point for fist attachment.
    /// Returns true if a surface is within attach radius.
    /// </summary>
    public bool TryGetNearestSurface(Vector2 fistPos, out Vector2 surfacePoint)
    {
        surfacePoint = fistPos;
        float bestDist = AttachRadius;
        bool found = false;

        // Left wall
        float d = Mathf.Abs(fistPos.X - Bounds.Position.X);
        if (d < bestDist && fistPos.Y >= Bounds.Position.Y && fistPos.Y <= Bounds.End.Y)
        {
            bestDist = d;
            surfacePoint = new Vector2(Bounds.Position.X, fistPos.Y);
            found = true;
        }

        // Right wall
        d = Mathf.Abs(fistPos.X - Bounds.End.X);
        if (d < bestDist && fistPos.Y >= Bounds.Position.Y && fistPos.Y <= Bounds.End.Y)
        {
            bestDist = d;
            surfacePoint = new Vector2(Bounds.End.X, fistPos.Y);
            found = true;
        }

        // Ceiling
        d = Mathf.Abs(fistPos.Y - Bounds.Position.Y);
        if (d < bestDist && fistPos.X >= Bounds.Position.X && fistPos.X <= Bounds.End.X)
        {
            bestDist = d;
            surfacePoint = new Vector2(fistPos.X, Bounds.Position.Y);
            found = true;
        }

        // Floor
        d = Mathf.Abs(fistPos.Y - Bounds.End.Y);
        if (d < bestDist && fistPos.X >= Bounds.Position.X && fistPos.X <= Bounds.End.X)
        {
            bestDist = d;
            surfacePoint = new Vector2(fistPos.X, Bounds.End.Y);
            found = true;
        }

        return found;
    }

    /// <summary>Check if a position is near a specific wall/ceiling.</summary>
    public bool IsNearWallLeft(Vector2 pos, float threshold = 60f) =>
        pos.X - Bounds.Position.X < threshold;

    public bool IsNearWallRight(Vector2 pos, float threshold = 60f) =>
        Bounds.End.X - pos.X < threshold;

    public bool IsNearCeiling(Vector2 pos, float threshold = 60f) =>
        pos.Y - Bounds.Position.Y < threshold;
}
