// ─────────────────────────────────────────────────────────────────────────────
// Arena.cs — Arena geometry and boundary enforcement
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation;

public class Arena
{
    public float Width { get; }
    public float Height { get; }
    public Rect2 Bounds { get; }
    public float WallThickness { get; } = 10f;

    public Arena(float width = 1500f, float height = 680f)  // Season 2: was 1200f
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
}
