// ─────────────────────────────────────────────────────────────────────────────
// SimPhysics.cs — Physics constants and collision helpers
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation;

public static class SimPhysics
{
    // Tuning constants
    public const float Gravity = 980f;         // pixels/sec^2
    public const float FixedDt = 1f / 60f;
    public const float FistDamage = 12f;
    public const float MoveForce = 400f;       // horizontal movement force
    public const float JumpImpulse = -420f;    // upward impulse (Y-down)
    public const float AirMoveForce = 200f;
    public const float MaxHorizontalSpeed = 350f;
    public const float GroundFriction = 0.85f;
    public const float AirFriction = 0.995f;
    public const float GrapplePullForce = 1800f;
    public const float SwingDamping = 0.995f;

    /// <summary>Apply gravity and integrate velocity into position.</summary>
    public static void Integrate(ref Vector2 pos, ref Vector2 vel, float dt, bool isGrounded)
    {
        // Gravity
        vel += new Vector2(0, Gravity * dt);

        // Friction
        float friction = isGrounded ? GroundFriction : AirFriction;
        vel = new Vector2(vel.X * friction, vel.Y);

        // Clamp horizontal speed
        vel = new Vector2(Mathf.Clamp(vel.X, -MaxHorizontalSpeed, MaxHorizontalSpeed), vel.Y);

        // Integrate
        pos += vel * dt;
    }

    /// <summary>
    /// Apply chain constraint: if attached+locked, enforce pendulum swing.
    /// If attached+retracting, pull body toward anchor.
    /// </summary>
    public static void ApplyChainConstraint(Fighter fighter, Fist fist, float dt)
    {
        if (!fist.IsAttachedToWorld) return;

        Vector2 anchor = fist.AnchorPoint;
        Vector2 toAnchor = anchor - fighter.Position;
        float dist = toAnchor.Length();

        if (dist < 1f) return;

        Vector2 dir = toAnchor / dist;

        if (fist.ChainState == FistChainState.Locked)
        {
            // Pendulum constraint: keep body at chain length from anchor
            if (dist > fist.ChainLength)
            {
                // Pull body back to chain length distance
                fighter.Position = anchor - dir * fist.ChainLength;

                // Remove radial velocity component (keep tangential for swing)
                float radialVel = fighter.Velocity.Dot(dir);
                if (radialVel < 0) // moving away from anchor
                {
                    fighter.Velocity -= dir * radialVel;
                }
            }

            // Damping
            fighter.Velocity *= SwingDamping;
        }
        else if (fist.ChainState == FistChainState.Retracting)
        {
            // Pull body toward anchor with force
            fighter.Velocity += dir * GrapplePullForce * dt;

            // Also enforce distance constraint: body can't be farther than chain length
            if (dist > fist.ChainLength && fist.ChainLength > 1f)
            {
                fighter.Position = anchor - dir * fist.ChainLength;
                // Remove outward radial velocity
                float radialVel = fighter.Velocity.Dot(dir);
                if (radialVel < 0)
                    fighter.Velocity -= dir * radialVel;
            }
        }
    }

    /// <summary>Check if two fists collide. Force both to retract.</summary>
    public static bool CheckFistCollision(Fist a, Fist b)
    {
        if (a.ChainState == FistChainState.Retracted || b.ChainState == FistChainState.Retracted)
            return false;

        float dist = a.Position.DistanceTo(b.Position);
        if (dist < a.FistRadius + b.FistRadius)
        {
            a.ForceRetract();
            b.ForceRetract();
            return true;
        }
        return false;
    }

    /// <summary>Check if an extending fist hits an opponent's body.</summary>
    public static bool CheckFistHitBody(Fist fist, Fighter target, out float damage)
    {
        damage = 0;
        if (fist.ChainState != FistChainState.Extending && fist.ChainState != FistChainState.Locked)
            return false;

        float dist = fist.Position.DistanceTo(target.Position);
        if (dist < fist.FistRadius + target.BodyRadius)
        {
            damage = FistDamage;
            target.ApplyDamage(damage);
            fist.ForceRetract();

            // Knockback
            Vector2 knockDir = (target.Position - fist.Position).Normalized();
            target.Velocity += knockDir * 250f;
            return true;
        }
        return false;
    }

    /// <summary>Auto-attach fist to surface when extending near a wall.</summary>
    public static void CheckFistSurfaceAttach(Fist fist, Arena arena)
    {
        if (fist.ChainState != FistChainState.Extending || fist.IsAttachedToWorld) return;

        if (arena.TryGetNearestSurface(fist.Position, out Vector2 surfacePoint))
        {
            fist.Attach(surfacePoint);
        }
    }
}
