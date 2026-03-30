// ─────────────────────────────────────────────────────────────────────────────
// BeaconPhysics.cs — Physics for Beacon Brawl v2 (roles, projectiles, parry, rifle)
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Godot;

namespace AiBtGym.Simulation.BeaconBrawl;

public static class BeaconPhysics
{
    // Core physics constants — tripled move/jump for snappier beacon brawl
    public const float Gravity = SimPhysics.Gravity;
    public const float FixedDt = SimPhysics.FixedDt;
    public const float MoveForce = SimPhysics.MoveForce * 3f;      // 1200 (was 400)
    public const float JumpImpulse = -600f;
    public const float AirMoveForce = SimPhysics.AirMoveForce * 3f; // 150 (was 50)
    public const float MaxHorizontalSpeed = SimPhysics.MaxHorizontalSpeed * 2f; // 700 (was 350)
    public const float GroundFriction = SimPhysics.GroundFriction;
    public const float AirFriction = SimPhysics.AirFriction;
    public const float GrapplePullForce = SimPhysics.GrapplePullForce;
    public const float SwingDamping = SimPhysics.SwingDamping;

    // Body collision
    public const float BodyBounceForce = 400f;
    public const float StunSpeedThreshold = 300f;

    // Parry lockout duration applied to the attacking weapon
    public const int ParryLockoutDuration = 120; // 2 seconds

    // Regen rates
    public const float BeaconRegenPerSecond = 1f;  // per owned beacon
    public const float BaseRegenPerSecond = 10f;

    /// <summary>Apply gravity, friction, velocity clamping, and integrate position.</summary>
    public static void Integrate(ref Vector2 pos, ref Vector2 vel, float dt, bool isGrounded)
    {
        vel += new Vector2(0, Gravity * dt);
        float friction = isGrounded ? GroundFriction : AirFriction;
        vel = new Vector2(vel.X * friction, vel.Y);
        vel = new Vector2(Mathf.Clamp(vel.X, -MaxHorizontalSpeed, MaxHorizontalSpeed), vel.Y);
        pos += vel * dt;
    }

    /// <summary>Apply chain constraint (pendulum/pull) for grappling hook.</summary>
    public static void ApplyChainConstraint(Pawn pawn, Fist hook, float dt)
    {
        if (!hook.IsAttachedToWorld) return;

        Vector2 anchor = hook.AnchorPoint;
        Vector2 toAnchor = anchor - pawn.Position;
        float dist = toAnchor.Length();
        if (dist < 1f) return;

        Vector2 dir = toAnchor / dist;

        if (hook.ChainState == FistChainState.Locked)
        {
            if (dist > hook.ChainLength)
            {
                pawn.Position = anchor - dir * hook.ChainLength;
                float radialVel = pawn.Velocity.Dot(dir);
                if (radialVel < 0)
                    pawn.Velocity -= dir * radialVel;
            }
            pawn.Velocity *= SwingDamping;
        }
        else if (hook.ChainState == FistChainState.Retracting)
        {
            pawn.Velocity += dir * GrapplePullForce * dt;
            if (dist > hook.ChainLength && hook.ChainLength > 1f)
            {
                pawn.Position = anchor - dir * hook.ChainLength;
                float radialVel = pawn.Velocity.Dot(dir);
                if (radialVel < 0)
                    pawn.Velocity -= dir * radialVel;
            }
        }
    }

    /// <summary>Body-body collision between two pawns. Skips dead pawns.</summary>
    public static bool CheckBodyCollision(Pawn a, Pawn b)
    {
        if (a.IsDead || b.IsDead) return false;

        float dist = a.Position.DistanceTo(b.Position);
        float minDist = a.BodyRadius + b.BodyRadius;
        if (dist >= minDist || dist < 0.1f) return false;

        // Separate bodies
        Vector2 dir = (b.Position - a.Position).Normalized();
        float overlap = minDist - dist;
        a.Position -= dir * (overlap / 2f);
        b.Position += dir * (overlap / 2f);

        // Mutual knockback
        Vector2 relVel = a.Velocity - b.Velocity;
        float impactSpeed = Mathf.Abs(relVel.Dot(dir));

        a.Velocity -= dir * BodyBounceForce * FixedDt;
        b.Velocity += dir * BodyBounceForce * FixedDt;

        a.TryStun(impactSpeed, StunSpeedThreshold);
        b.TryStun(impactSpeed, StunSpeedThreshold);

        return true;
    }

    /// <summary>Check if two hooks collide. Force both to retract.</summary>
    public static bool CheckHookCollision(Fist a, Fist b)
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

    // ── Grappler Fist (melee) ──

    /// <summary>Execute a melee punch against nearby enemies. Returns the pawn hit, or null.</summary>
    public static Pawn? ExecuteFistPunch(Pawn attacker, List<Pawn> enemies)
    {
        if (attacker.FistCooldown > 0 || attacker.FistLockoutTicks > 0) return null;

        Pawn? bestTarget = null;
        float bestDist = Pawn.FistRange;

        foreach (var enemy in enemies)
        {
            if (enemy.IsDead) continue;
            float dist = attacker.Position.DistanceTo(enemy.Position);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestTarget = enemy;
            }
        }

        if (bestTarget == null) return null;

        // Check parry
        if (bestTarget.IsParryActive)
        {
            attacker.FistLockoutTicks = ParryLockoutDuration;
            attacker.FistCooldown = Pawn.FistCooldownMax;
            return null;
        }

        bestTarget.TakeDamage(Pawn.FistDamage);
        Vector2 knockDir = (bestTarget.Position - attacker.Position).Normalized();
        bestTarget.ApplyKnockback(knockDir, Pawn.FistKnockback);
        attacker.FistCooldown = Pawn.FistCooldownMax;
        return bestTarget;
    }

    // ── Grappler Hook Grab (point-blank CC) ──

    /// <summary>Check if hook extending near an enemy triggers a grab. Returns grabbed pawn or null.</summary>
    public static Pawn? CheckHookGrab(Pawn grappler, List<Pawn> enemies)
    {
        if (grappler.Hook.ChainState != FistChainState.Extending) return null;
        if (grappler.HookLockoutTicks > 0) return null;

        foreach (var enemy in enemies)
        {
            if (enemy.IsDead) continue;
            float dist = grappler.Hook.Position.DistanceTo(enemy.Position);
            if (dist > Pawn.GrappleGrabRange) continue;

            // Check parry
            if (enemy.IsParryActive)
            {
                grappler.HookLockoutTicks = ParryLockoutDuration;
                grappler.Hook.ForceRetract();
                return null;
            }

            // Grab: root self, make enemy vulnerable
            grappler.GrabRootTicks = Pawn.GrabRootDuration;
            grappler.Hook.ForceRetract();
            enemy.VulnerableTicks = Pawn.VulnerableDuration;
            return enemy;
        }
        return null;
    }

    // ── Projectile System ──

    /// <summary>Tick all projectiles: move, check wall collision, check pawn collision.</summary>
    public static void TickProjectiles(List<Projectile> projectiles, Pawn[] allPawns, BeaconArena arena,
        BeaconRecorder? recorder = null, int tick = 0)
    {
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            var proj = projectiles[i];
            if (!proj.Tick(FixedDt))
            {
                projectiles.RemoveAt(i);
                continue;
            }

            // Check wall collision (destroy projectile)
            if (!arena.Bounds.HasPoint(proj.Position))
            {
                proj.IsAlive = false;
                projectiles.RemoveAt(i);
                continue;
            }

            // Check platform collision
            if (arena.Platform.HasPoint(proj.Position))
            {
                proj.IsAlive = false;
                projectiles.RemoveAt(i);
                continue;
            }

            // Check pawn collision
            bool removed = false;
            foreach (var pawn in allPawns)
            {
                if (pawn.IsDead || pawn.TeamIndex == proj.OwnerTeam) continue;
                float dist = proj.Position.DistanceTo(pawn.Position);
                if (dist > pawn.BodyRadius + proj.Radius) continue;

                // Check parry
                if (pawn.IsParryActive)
                {
                    // Find the shooter and lockout their weapon
                    foreach (var shooter in allPawns)
                    {
                        if (shooter.TeamIndex == proj.OwnerTeam && shooter.PawnIndex == proj.OwnerPawnIndex)
                        {
                            if (proj.WeaponType == "pistol")
                                shooter.PistolLockoutTicks = ParryLockoutDuration;
                            break;
                        }
                    }
                    recorder?.RecordEvent(tick, pawn.TeamIndex, -1, "parry_success");
                    proj.IsAlive = false;
                    projectiles.RemoveAt(i);
                    removed = true;
                    break;
                }

                pawn.TakeDamage(proj.Damage);
                Vector2 knockDir = proj.Velocity.Normalized();
                pawn.ApplyKnockback(knockDir, proj.Knockback);
                recorder?.RecordEvent(tick, proj.OwnerTeam, proj.OwnerPawnIndex,
                    $"pistol_hit_{pawn.TeamIndex}_{pawn.PawnIndex}");
                recorder?.RecordDamage(proj.OwnerTeam, proj.Damage);
                proj.IsAlive = false;
                projectiles.RemoveAt(i);
                removed = true;
                break;
            }

            if (removed) continue;
        }
    }

    // ── Rifle (raytrace + ricochet) ──

    /// <summary>Fire a rifle ray with up to maxBounces ricochets. Returns hit pawn or null.</summary>
    public static Pawn? FireRifle(Pawn shooter, Vector2 aimDir, Pawn[] allPawns, BeaconArena arena,
        out List<Vector2> raySegments)
    {
        raySegments = [];

        Vector2 origin = shooter.Position;
        Vector2 dir = aimDir.Normalized();
        float remaining = Pawn.RifleRange;

        // Apply recoil to shooter (opposite direction)
        shooter.Velocity -= dir * Pawn.RifleRecoilForce;

        raySegments.Add(origin);

        for (int bounce = 0; bounce <= Pawn.RifleMaxBounces; bounce++)
        {
            // Check pawn hits along this segment
            Pawn? hitPawn = null;
            float hitPawnDist = remaining;

            foreach (var pawn in allPawns)
            {
                if (pawn.IsDead || pawn.TeamIndex == shooter.TeamIndex) continue;
                float dist = RayCircleIntersect(origin, dir, pawn.Position, pawn.BodyRadius);
                if (dist >= 0 && dist < hitPawnDist)
                {
                    hitPawnDist = dist;
                    hitPawn = pawn;
                }
            }

            // Check wall hit
            bool wallHit = arena.RaycastWall(origin, dir, remaining,
                out Vector2 wallPoint, out Vector2 wallNormal, out float wallDist);

            // Pawn hit is closer than wall
            if (hitPawn != null && hitPawnDist < wallDist)
            {
                Vector2 hitPoint = origin + dir * hitPawnDist;
                raySegments.Add(hitPoint);

                // Check parry
                if (hitPawn.IsParryActive)
                {
                    shooter.RifleLockoutTicks = ParryLockoutDuration;
                    return null;
                }

                hitPawn.TakeDamage(Pawn.RifleDamage);
                hitPawn.ApplyKnockback(dir, Pawn.RifleKnockback);
                return hitPawn;
            }

            // Wall hit — ricochet if bounces remain
            if (wallHit)
            {
                raySegments.Add(wallPoint);

                if (bounce < Pawn.RifleMaxBounces)
                {
                    // Reflect direction
                    dir = dir - 2f * dir.Dot(wallNormal) * wallNormal;
                    dir = dir.Normalized();
                    origin = wallPoint + dir * 0.1f; // nudge off wall
                    remaining -= wallDist;
                }
                else
                {
                    // No more bounces, ray ends
                    return null;
                }
            }
            else
            {
                // Ray extends to max range without hitting anything
                raySegments.Add(origin + dir * remaining);
                return null;
            }
        }

        return null;
    }

    /// <summary>Ray-circle intersection. Returns distance to intersection or -1.</summary>
    private static float RayCircleIntersect(Vector2 origin, Vector2 dir, Vector2 center, float radius)
    {
        Vector2 oc = origin - center;
        float a = dir.Dot(dir);
        float b = 2f * oc.Dot(dir);
        float c = oc.Dot(oc) - radius * radius;
        float disc = b * b - 4f * a * c;
        if (disc < 0) return -1f;

        float t = (-b - Mathf.Sqrt(disc)) / (2f * a);
        return t >= 0 ? t : -1f;
    }

    // ── Regen ──

    /// <summary>Apply beacon regen (slow, anywhere on map) and base regen (fast, in base zone).</summary>
    public static void ApplyRegen(Pawn pawn, int ownedBeaconCount, bool inBaseZone, float dt)
    {
        if (pawn.IsDead || pawn.Health >= Pawn.MaxHealth) return;

        float regen = 0f;

        // Beacon regen: +1 HP/s per owned beacon
        regen += ownedBeaconCount * BeaconRegenPerSecond * dt;

        // Base regen: +10 HP/s when standing in base
        if (inBaseZone)
            regen += BaseRegenPerSecond * dt;

        if (regen > 0f)
        {
            pawn.Health = Mathf.Min(Pawn.MaxHealth, pawn.Health + regen);
        }
    }
}
