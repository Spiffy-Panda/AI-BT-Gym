// ─────────────────────────────────────────────────────────────────────────────
// Pawn.cs — Team pawn with role-based weapons, health, parry, and status effects
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation.BeaconBrawl;

public enum PawnRole { Grappler, Gunner }

public class Pawn
{
    public int TeamIndex { get; set; }
    public int PawnIndex { get; set; }
    public PawnRole Role { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public bool IsGrounded { get; set; }
    public float BodyRadius { get; set; } = 16f;

    // ── Health ──
    public const float MaxHealth = 100f;
    public float Health { get; set; } = MaxHealth;
    public bool IsDead { get; set; }
    public int RespawnTimer { get; set; }
    public const int RespawnDuration = 180; // 3 seconds at 60fps

    // ── Grappler weapons ──
    public Fist Hook { get; } = new();       // grappling hook (reuses Fist chain mechanics)
    public int FistCooldown { get; set; }
    public const int FistCooldownMax = 15;    // 0.25s — fast melee cycle
    public const float FistRange = 40f;
    public const float FistDamage = 8f;
    public const float FistKnockback = 10f;   // minimal default

    // ── Grappler hook grab (point-blank CC) ──
    public const float GrappleGrabRange = 50f;
    public const int GrabRootDuration = 18;   // 0.3s root on self
    public const int VulnerableDuration = 60; // 1s vulnerable on target
    public const float VulnerableMultiplier = 1.5f;
    public int GrabRootTicks { get; set; }    // self-root timer while grabbing

    // ── Gunner weapons ──
    public int PistolCooldown { get; set; }
    public const int PistolCooldownMax = 30;  // 0.5s
    public const float PistolDamage = 6f;
    public const float PistolSpeed = 1060f;    // outer beacon distance range: 1060²/980 ≈ 1147px
    public const float PistolKnockback = 10f; // minimal default
    public const int PistolLifetime = 180;    // 3 seconds max

    public int RifleCooldown { get; set; }
    public const int RifleCooldownMax = 90;   // 1.5s
    public const float RifleDamage = 18f;
    public const float RifleRange = 6000f;    // 2x original (effectively unlimited)
    public const int RifleMaxBounces = 2;
    public const float RifleRecoilForce = 300f;
    public const float RifleKnockback = 10f;  // minimal default
    public const int RifleChargeTime = 30;    // 0.5s delay between fire command and shot

    /// <summary>Ticks remaining until queued rifle shot fires. 0 = not charging.</summary>
    public int RifleChargeTicks { get; set; }
    /// <summary>Aim direction locked in at charge start.</summary>
    public Vector2 RifleChargeDir { get; set; }

    // ── Parry (shared) ──
    public int ParryCooldown { get; set; }
    public const int ParryCooldownMax = 180;  // 3s cooldown
    public const int ParryWindowDuration = 15; // 0.25s active window
    public int ParryActiveTicks { get; set; } // counts down during window
    public bool IsParryActive => ParryActiveTicks > 0;

    // ── Status effects ──
    public int VulnerableTicks { get; set; }
    public bool IsVulnerable => VulnerableTicks > 0;

    // Per-weapon lockout (from enemy parry)
    public int HookLockoutTicks { get; set; }
    public int FistLockoutTicks { get; set; }
    public int PistolLockoutTicks { get; set; }
    public int RifleLockoutTicks { get; set; }

    // Stun state (from body collisions)
    public bool IsStunned { get; set; }
    public int StunTicksRemaining { get; set; }
    public const int StunDuration = 30; // 0.5 seconds

    // ── Knockback tuning ──
    /// <summary>Global scaling for health-based knockback. 0 = disabled (pure damage meta).</summary>
    public static float KnockbackHealthScaling = 0f;

    public Pawn(int teamIndex, int pawnIndex, PawnRole role, Vector2 startPos)
    {
        TeamIndex = teamIndex;
        PawnIndex = pawnIndex;
        Role = role;
        Position = startPos;
    }

    /// <summary>Apply damage, accounting for vulnerability. Returns actual damage dealt.</summary>
    public float TakeDamage(float baseDamage)
    {
        if (IsDead) return 0f;
        float actual = IsVulnerable ? baseDamage * VulnerableMultiplier : baseDamage;
        Health -= actual;
        if (Health <= 0f)
        {
            Health = 0f;
            IsDead = true;
            RespawnTimer = RespawnDuration;
        }
        return actual;
    }

    /// <summary>Apply knockback force, scaled by target health.</summary>
    public void ApplyKnockback(Vector2 direction, float baseForce)
    {
        if (IsDead) return;
        float healthPct = Health / MaxHealth;
        float scale = 1f + (1f - healthPct) * KnockbackHealthScaling;
        Velocity += direction.Normalized() * baseForce * scale;
    }

    /// <summary>Respawn at the given position with full health.</summary>
    public void Respawn(Vector2 spawnPos)
    {
        IsDead = false;
        Health = MaxHealth;
        Position = spawnPos;
        Velocity = Vector2.Zero;
        RespawnTimer = 0;
        VulnerableTicks = 0;
        HookLockoutTicks = 0;
        FistLockoutTicks = 0;
        PistolLockoutTicks = 0;
        RifleLockoutTicks = 0;
        RifleChargeTicks = 0;
        RifleChargeDir = Vector2.Zero;
        IsStunned = false;
        StunTicksRemaining = 0;
        GrabRootTicks = 0;
        ParryActiveTicks = 0;
        Hook.ForceRetract();
    }

    /// <summary>Activate parry window.</summary>
    public bool ActivateParry()
    {
        if (ParryCooldown > 0 || IsParryActive) return false;
        ParryActiveTicks = ParryWindowDuration;
        ParryCooldown = ParryCooldownMax;
        return true;
    }

    /// <summary>Apply stun if velocity exceeds threshold after impact.</summary>
    public void TryStun(float impactSpeed, float threshold = 300f)
    {
        if (impactSpeed > threshold && !IsStunned)
        {
            IsStunned = true;
            StunTicksRemaining = StunDuration;
        }
    }

    /// <summary>Tick all timers (stun, cooldowns, lockouts, status effects).</summary>
    public void TickTimers()
    {
        // Stun
        if (IsStunned)
        {
            StunTicksRemaining--;
            if (StunTicksRemaining <= 0) { IsStunned = false; StunTicksRemaining = 0; }
        }

        // Respawn countdown
        if (IsDead && RespawnTimer > 0) RespawnTimer--;

        // Vulnerability
        if (VulnerableTicks > 0) VulnerableTicks--;

        // Grab root
        if (GrabRootTicks > 0) GrabRootTicks--;

        // Parry window
        if (ParryActiveTicks > 0) ParryActiveTicks--;

        // Rifle charge countdown (handled externally — just tick here)
        if (RifleChargeTicks > 0) RifleChargeTicks--;

        // Cooldowns
        if (ParryCooldown > 0) ParryCooldown--;
        if (FistCooldown > 0) FistCooldown--;
        if (PistolCooldown > 0) PistolCooldown--;
        if (RifleCooldown > 0) RifleCooldown--;

        // Weapon lockouts
        if (HookLockoutTicks > 0) HookLockoutTicks--;
        if (FistLockoutTicks > 0) FistLockoutTicks--;
        if (PistolLockoutTicks > 0) PistolLockoutTicks--;
        if (RifleLockoutTicks > 0) RifleLockoutTicks--;
    }

    /// <summary>Whether the pawn can act (not dead, not stunned, not grab-rooted).</summary>
    public bool CanAct => !IsDead && !IsStunned && GrabRootTicks <= 0;

    public float HealthPct => MaxHealth > 0 ? Health / MaxHealth : 0f;
}
