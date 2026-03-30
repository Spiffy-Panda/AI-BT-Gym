// ─────────────────────────────────────────────────────────────────────────────
// Projectile.cs — Ballistic projectile entity for Beacon Brawl (pistol bullets)
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation.BeaconBrawl;

public class Projectile
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float Gravity { get; set; }
    public float Damage { get; set; }
    public float Knockback { get; set; }
    public int OwnerTeam { get; set; }
    public int OwnerPawnIndex { get; set; }
    public int LifetimeRemaining { get; set; }
    public bool IsAlive { get; set; } = true;
    public float Radius { get; set; } = 4f;

    /// <summary>The weapon type that spawned this projectile (for parry lockout).</summary>
    public string WeaponType { get; set; } = "pistol";

    public Projectile(Vector2 position, Vector2 velocity, float gravity, float damage,
        float knockback, int ownerTeam, int ownerPawnIndex, int lifetime)
    {
        Position = position;
        Velocity = velocity;
        Gravity = gravity;
        Damage = damage;
        Knockback = knockback;
        OwnerTeam = ownerTeam;
        OwnerPawnIndex = ownerPawnIndex;
        LifetimeRemaining = lifetime;
    }

    /// <summary>Advance projectile by one tick. Returns false if expired.</summary>
    public bool Tick(float dt)
    {
        if (!IsAlive) return false;

        // Apply gravity (Y-down coordinate system)
        Velocity += new Vector2(0, Gravity * dt);

        // Integrate position
        Position += Velocity * dt;

        LifetimeRemaining--;
        if (LifetimeRemaining <= 0) IsAlive = false;

        return IsAlive;
    }
}
