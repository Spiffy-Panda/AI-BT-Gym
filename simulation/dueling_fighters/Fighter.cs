// ─────────────────────────────────────────────────────────────────────────────
// Fighter.cs — Fighter body with two fists
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation;

public class Fighter
{
    public int Index { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Health { get; set; } = 100f;
    public bool IsGrounded { get; set; }
    public float BodyRadius { get; set; } = 18f;

    // Hazard burn (persists after leaving zone)
    public int HazardBurnTicks { get; set; }
    public float HazardBurnRate { get; set; }
    public const int HazardBurnDuration = 60; // 1 second at 60fps

    public Fist LeftFist { get; } = new();
    public Fist RightFist { get; } = new();

    public Fighter(int index, Vector2 startPos)
    {
        Index = index;
        Position = startPos;
    }

    public void ApplyDamage(float amount)
    {
        Health = Mathf.Clamp(Health - amount, 0f, 100f);
    }

    public Fist GetFist(string hand) =>
        hand.ToLowerInvariant() == "left" ? LeftFist : RightFist;
}
