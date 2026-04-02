// ─────────────────────────────────────────────────────────────────────────────
// PawnBtContext.cs — Bridges BT leaf nodes to Beacon Brawl v2 simulation
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation.BeaconBrawl;

public class PawnBtContext : IBtContext
{
    private readonly Pawn _pawn;
    private readonly List<Pawn> _allies;
    private readonly List<Pawn> _enemies;
    private readonly Beacon[] _beacons;
    private readonly BeaconArena _arena;
    private readonly int _tick;
    private readonly int[] _teamScores;
    private readonly int[] _rates;
    private readonly BeaconMatch? _match;

    /// <summary>Active projectiles list — pistol shots append here.</summary>
    public List<Projectile> Projectiles { get; }

    /// <summary>All pawns array — needed for rifle raycasting.</summary>
    public Pawn[] AllPawns { get; }

    public int CurrentTick => _tick;
    public Action<string>? OnActionExecuted { get; set; }

    /// <summary>Callback for pistol fire events (position + velocity for ballistic replay).</summary>
    public Action<Vector2, Vector2>? OnPistolFired { get; set; }

    /// <summary>Callback for rifle fire events (segments + hit pawn, null if miss).</summary>
    public Action<List<Vector2>, Pawn?>? OnRifleFired { get; set; }

    /// <summary>Callback for hook grab events (for recording).</summary>
    public Action<Pawn>? OnHookGrab { get; set; }

    /// <summary>Callback for fist hit events (for recording).</summary>
    public Action<Pawn>? OnFistHit { get; set; }

    /// <summary>Callback for damage dealt (attackerTeam, amount).</summary>
    public Action<int, float>? OnDamageDealt { get; set; }

    /// <summary>Callback for parry success (defender team).</summary>
    public Action<int>? OnParrySuccess { get; set; }

    /// <summary>Callback for pistol hit.</summary>
    public Action<Pawn>? OnPistolHit { get; set; }

    // Cached nearest enemy (computed once per tick)
    private Pawn? _nearestEnemy;
    private float _nearestEnemyDist;

    /// <summary>Base zone index for this pawn, accounting for spawn swap.</summary>
    private int MyBaseIndex;

    public PawnBtContext(Pawn pawn, List<Pawn> allies, List<Pawn> enemies,
        Beacon[] beacons, BeaconArena arena, int tick, int[] teamScores,
        int[]? rates, List<Projectile> projectiles, Pawn[] allPawns,
        BeaconMatch? match = null)
    {
        _pawn = pawn;
        _allies = allies;
        _enemies = enemies;
        _beacons = beacons;
        _arena = arena;
        _tick = tick;
        _teamScores = teamScores;
        _rates = rates ?? [0, 0];
        _match = match;
        Projectiles = projectiles;
        AllPawns = allPawns;

        // Resolve base zone index accounting for spawn swap
        bool swapped = match?.SpawnSwapped == true;
        MyBaseIndex = swapped ? (1 - pawn.TeamIndex) : pawn.TeamIndex;

        // Cache nearest living enemy
        _nearestEnemyDist = float.MaxValue;
        foreach (var e in _enemies)
        {
            if (e.IsDead) continue;
            float d = pawn.Position.DistanceTo(e.Position);
            if (d < _nearestEnemyDist) { _nearestEnemyDist = d; _nearestEnemy = e; }
        }
    }

    public float ResolveVariable(string name)
    {
        int myTeam = _pawn.TeamIndex + 1;
        int enemyTeam = myTeam == 1 ? 2 : 1;

        return name switch
        {
            // Self state
            "pos_x" => _pawn.Position.X,
            "pos_y" => _pawn.Position.Y,
            "vel_x" => _pawn.Velocity.X,
            "vel_y" => _pawn.Velocity.Y,
            "is_grounded" => _pawn.IsGrounded ? 1f : 0f,
            "is_stunned" => _pawn.IsStunned ? 1f : 0f,
            "my_role" => (float)_pawn.Role,

            // Health
            "my_health" => _pawn.Health,
            "my_health_pct" => _pawn.HealthPct,
            "am_dead" => _pawn.IsDead ? 1f : 0f,
            "ally_health" => _allies.Count > 0 ? _allies.Where(a => !a.IsDead).Select(a => a.Health).FirstOrDefault() : 0f,
            "ally_health_pct" => _allies.Count > 0 ? _allies.Where(a => !a.IsDead).Select(a => a.HealthPct).FirstOrDefault() : 0f,
            "ally_dead" => _allies.Count > 0 && _allies[0].IsDead ? 1f : 0f,
            "enemy_health" => _nearestEnemy?.Health ?? 0f,
            "enemy_health_pct" => _nearestEnemy?.HealthPct ?? 0f,
            "enemy_dead" => (_enemies.Count > 0 && _enemies[0].IsDead) ? 1f : 0f,
            "enemy2_dead" => (_enemies.Count > 1 && _enemies[1].IsDead) ? 1f : 0f,

            // Status effects
            "is_vulnerable" => _pawn.IsVulnerable ? 1f : 0f,
            "is_weapon_locked" => (_pawn.HookLockoutTicks > 0 || _pawn.FistLockoutTicks > 0 ||
                                   _pawn.PistolLockoutTicks > 0 || _pawn.RifleLockoutTicks > 0) ? 1f : 0f,
            "nearest_enemy_vulnerable" => _nearestEnemy?.IsVulnerable == true ? 1f : 0f,
            "nearest_enemy_weapon_locked" => _nearestEnemy != null && (
                _nearestEnemy.HookLockoutTicks > 0 || _nearestEnemy.FistLockoutTicks > 0 ||
                _nearestEnemy.PistolLockoutTicks > 0 || _nearestEnemy.RifleLockoutTicks > 0) ? 1f : 0f,

            // Parry
            "parry_ready" => _pawn.ParryCooldown <= 0 && !_pawn.IsParryActive ? 1f : 0f,
            "parry_cooldown" => _pawn.ParryCooldown,
            "parry_active" => _pawn.IsParryActive ? 1f : 0f,

            // Grappler weapons
            "hook_state" => (float)_pawn.Hook.ChainState,
            "hook_ready" => _pawn.Hook.ChainState == FistChainState.Retracted && _pawn.HookLockoutTicks <= 0 ? 1f : 0f,
            "hook_retracted" => _pawn.Hook.ChainState == FistChainState.Retracted ? 1f : 0f,
            "hook_attached" => _pawn.Hook.IsAttachedToWorld ? 1f : 0f,
            "hook_chain" => _pawn.Hook.ChainLength,
            "fist_ready" => _pawn.FistCooldown <= 0 && _pawn.FistLockoutTicks <= 0 ? 1f : 0f,
            "fist_cooldown" => _pawn.FistCooldown,

            // Gunner weapons
            "pistol_ready" => _pawn.PistolCooldown <= 0 && _pawn.PistolLockoutTicks <= 0 ? 1f : 0f,
            "pistol_cooldown" => _pawn.PistolCooldown,
            "rifle_ready" => _pawn.RifleCooldown <= 0 && _pawn.RifleLockoutTicks <= 0 && _pawn.RifleChargeTicks <= 0 ? 1f : 0f,
            "rifle_cooldown" => _pawn.RifleCooldown,
            "rifle_charging" => _pawn.RifleChargeTicks > 0 ? 1f : 0f,

            // Scores
            "team_score" => _teamScores[_pawn.TeamIndex],
            "enemy_score" => _teamScores[1 - _pawn.TeamIndex],
            "score_diff" => _teamScores[_pawn.TeamIndex] - _teamScores[1 - _pawn.TeamIndex],
            "score_rate" => _rates[_pawn.TeamIndex],
            "enemy_score_rate" => _rates[1 - _pawn.TeamIndex],
            "rate_advantage" => _rates[_pawn.TeamIndex] - _rates[1 - _pawn.TeamIndex],
            "is_overtime" => _match?.IsOvertime == true ? 1f : 0f,

            // Beacon ownership
            "beacon_left_owner" => BeaconOwnerPerspective(_beacons[0].OwnerTeam, myTeam, enemyTeam),
            "beacon_center_owner" => BeaconOwnerPerspective(_beacons[1].OwnerTeam, myTeam, enemyTeam),
            "beacon_right_owner" => BeaconOwnerPerspective(_beacons[2].OwnerTeam, myTeam, enemyTeam),
            "beacon_left_progress" => _beacons[0].CaptureProgress,
            "beacon_center_progress" => _beacons[1].CaptureProgress,
            "beacon_right_progress" => _beacons[2].CaptureProgress,
            "beacon_left_contested" => _beacons[0].IsContested ? 1f : 0f,
            "beacon_center_contested" => _beacons[1].IsContested ? 1f : 0f,
            "beacon_right_contested" => _beacons[2].IsContested ? 1f : 0f,

            // Am I inside a beacon zone?
            "in_beacon_left" => _beacons[0].Contains(_pawn.Position) ? 1f : 0f,
            "in_beacon_center" => _beacons[1].Contains(_pawn.Position) ? 1f : 0f,
            "in_beacon_right" => _beacons[2].Contains(_pawn.Position) ? 1f : 0f,

            // Am I in base zone?
            "in_base_zone" => _arena.BaseZones[MyBaseIndex].Contains(_pawn.Position) ? 1f : 0f,
            "ally_in_base_zone" => _allies.Any(a => !a.IsDead && _arena.BaseZones[MyBaseIndex].Contains(a.Position)) ? 1f : 0f,

            // Beacon counts
            "owned_beacon_count" => _beacons.Count(b => b.OwnerTeam == myTeam),
            "enemy_beacon_count" => _beacons.Count(b => b.OwnerTeam == enemyTeam),

            // Distance to beacons
            "dist_to_beacon_left" => _pawn.Position.DistanceTo(_beacons[0].Zone.Center),
            "dist_to_beacon_center" => _pawn.Position.DistanceTo(_beacons[1].Zone.Center),
            "dist_to_beacon_right" => _pawn.Position.DistanceTo(_beacons[2].Zone.Center),

            // Pawn counts per beacon (only count living pawns)
            "ally_count_left_beacon" => CountInBeacon(_allies.Where(a => !a.IsDead), _beacons[0]) + (_beacons[0].Contains(_pawn.Position) && !_pawn.IsDead ? 1 : 0),
            "ally_count_center_beacon" => CountInBeacon(_allies.Where(a => !a.IsDead), _beacons[1]) + (_beacons[1].Contains(_pawn.Position) && !_pawn.IsDead ? 1 : 0),
            "ally_count_right_beacon" => CountInBeacon(_allies.Where(a => !a.IsDead), _beacons[2]) + (_beacons[2].Contains(_pawn.Position) && !_pawn.IsDead ? 1 : 0),
            "enemy_count_left_beacon" => CountInBeacon(_enemies.Where(e => !e.IsDead), _beacons[0]),
            "enemy_count_center_beacon" => CountInBeacon(_enemies.Where(e => !e.IsDead), _beacons[1]),
            "enemy_count_right_beacon" => CountInBeacon(_enemies.Where(e => !e.IsDead), _beacons[2]),

            // Nearest enemy
            "nearest_enemy_dist" => _nearestEnemyDist < float.MaxValue ? _nearestEnemyDist : 9999f,
            "nearest_enemy_dir_x" => _nearestEnemy != null ? (_nearestEnemy.Position.X > _pawn.Position.X ? 1f : -1f) : 0f,
            "nearest_enemy_dir_y" => _nearestEnemy != null ? (_nearestEnemy.Position.Y > _pawn.Position.Y ? 1f : -1f) : 0f,

            // Nearest unowned beacon
            "nearest_unowned_dist" => NearestUnownedBeaconDist(myTeam),
            "nearest_unowned_dir_x" => NearestUnownedBeaconDirX(myTeam),

            // Nearest ally
            "ally1_dist" => _allies.Where(a => !a.IsDead).Select(a => _pawn.Position.DistanceTo(a.Position)).DefaultIfEmpty(0f).Min(),
            "ally1_pos_x" => NearestAlly()?.Position.X ?? _pawn.Position.X,
            "ally1_pos_y" => NearestAlly()?.Position.Y ?? _pawn.Position.Y,

            // ── Map awareness: static counts ──
            "hazard_count" => _arena.Modifiers.HazardZones.Count,
            "pickup_count" => _arena.Modifiers.Pickups.Count,
            "wall_count" => _arena.Modifiers.DestructibleWalls.Count,

            // ── Map awareness: dynamic state ──
            "on_hazard" => (_arena.IsOnGround(_pawn.Position, _pawn.BodyRadius) && _arena.IsInHazardZone(_pawn.Position)) ? 1f : 0f,
            "burning" => _pawn.HazardBurnTicks > 0 ? 1f : 0f,
            "nearest_pickup_dist" => NearestActivePickupDist(),

            // Indexed variables handled below
            _ => ResolveIndexedVariable(name)
        };
    }

    private float ResolveIndexedVariable(string name)
    {
        // Pickup indexed: pickup_N_active, pickup_N_x, pickup_N_y, pickup_N_dist
        if (name.StartsWith("pickup_") && _match != null)
        {
            var parts = name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int idx)
                && idx >= 0 && idx < _arena.Modifiers.Pickups.Count
                && idx < _match.PickupActive.Length)
            {
                var pickup = _arena.Modifiers.Pickups[idx];
                return parts[2] switch
                {
                    "active" => _match.PickupActive[idx] ? 1f : 0f,
                    "x" => pickup.X,
                    "y" => pickup.Y,
                    "dist" => _pawn.Position.DistanceTo(new Vector2(pickup.X, pickup.Y)),
                    _ => 0f
                };
            }
        }

        // Destructible wall indexed: wall_N_hp, wall_N_exists
        if (name.StartsWith("wall_") && _match != null)
        {
            var parts = name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int idx)
                && idx >= 0 && idx < _arena.Modifiers.DestructibleWalls.Count
                && idx < _match.DestructibleWallHp.Length)
            {
                return parts[2] switch
                {
                    "hp" => _match.DestructibleWallHp[idx],
                    "exists" => _match.DestructibleWallExists[idx] ? 1f : 0f,
                    _ => 0f
                };
            }
        }

        return 0f;
    }

    private float NearestActivePickupDist()
    {
        if (_match == null) return 9999f;
        var pickups = _arena.Modifiers.Pickups;
        float best = 9999f;
        for (int i = 0; i < pickups.Count && i < _match.PickupActive.Length; i++)
        {
            if (!_match.PickupActive[i]) continue;
            float d = _pawn.Position.DistanceTo(new Vector2(pickups[i].X, pickups[i].Y));
            if (d < best) best = d;
        }
        return best;
    }

    public BtStatus ExecuteAction(string action)
    {
        if (!_pawn.CanAct) return BtStatus.Failure;

        var parts = action.Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();

        var result = ExecuteCmd(cmd);
        if (result == BtStatus.Success)
            OnActionExecuted?.Invoke(cmd);
        return result;
    }

    private BtStatus ExecuteCmd(string cmd)
    {
        return cmd switch
        {
            // Movement
            "move_left" => ApplyMove(-1f),
            "move_right" => ApplyMove(1f),
            "jump" => Jump(),
            "move_toward_nearest_enemy" => _nearestEnemy != null
                ? ApplyMove(_nearestEnemy.Position.X > _pawn.Position.X ? 1f : -1f)
                : BtStatus.Failure,
            "move_away_from_nearest_enemy" => _nearestEnemy != null
                ? ApplyMove(_nearestEnemy.Position.X > _pawn.Position.X ? -1f : 1f)
                : BtStatus.Failure,
            "move_toward_beacon_left" => MoveToward(_beacons[0].Zone.Center),
            "move_toward_beacon_center" => MoveToward(_beacons[1].Zone.Center),
            "move_toward_beacon_right" => MoveToward(_beacons[2].Zone.Center),
            "move_toward_nearest_unowned" => MoveTowardNearestUnowned(),
            "move_toward_nearest_contested" => MoveTowardNearestContested(),
            "move_toward_base" => MoveToward(_arena.BaseZones[MyBaseIndex].Center),
            "move_toward_nearest_pickup" => MoveTowardNearestPickup(),
            "move_off_hazard" => MoveOffHazard(),

            // Grappling hook (Grappler only, reuses Fist chain)
            "launch_hook_up" => LaunchHook(new Vector2(0, -1)),
            "launch_hook_toward_enemy" => _nearestEnemy != null
                ? LaunchHookAt(_nearestEnemy.Position) : BtStatus.Failure,
            "launch_hook_toward_center" => LaunchHookAt(_beacons[1].Zone.Center),
            "launch_hook_toward_beacon_left" => LaunchHookAt(_beacons[0].Zone.Center),
            "launch_hook_toward_beacon_right" => LaunchHookAt(_beacons[2].Zone.Center),
            "lock_hook" => _pawn.Hook.Lock() ? BtStatus.Success : BtStatus.Failure,
            "retract_hook" => _pawn.Hook.Retract() ? BtStatus.Success : BtStatus.Failure,
            "detach_hook" => Do(() => _pawn.Hook.Detach()),

            // Fist (Grappler melee)
            "punch_toward_enemy" => ExecutePunch(),
            "punch_left" => ExecutePunchDir(-1f),
            "punch_right" => ExecutePunchDir(1f),

            // Pistol (Gunner)
            "shoot_pistol_at_enemy" => _nearestEnemy != null
                ? FirePistolAt(_nearestEnemy.Position) : BtStatus.Failure,
            "shoot_pistol_ahead" => FirePistol(new Vector2(_pawn.Velocity.X >= 0 ? 1f : -1f, -0.3f).Normalized()),
            "shoot_pistol_up_left" => FirePistol(new Vector2(-0.7f, -0.7f).Normalized()),
            "shoot_pistol_up_right" => FirePistol(new Vector2(0.7f, -0.7f).Normalized()),

            // Rifle (Gunner)
            "shoot_rifle_at_enemy" => _nearestEnemy != null
                ? FireRifleAt(_nearestEnemy.Position) : BtStatus.Failure,
            "shoot_rifle_at_wall_bounce" => FireRifleWallBounce(),
            "shoot_rifle_left" => FireRifleDir(new Vector2(-1, 0)),
            "shoot_rifle_right" => FireRifleDir(new Vector2(1, 0)),
            "shoot_rifle_down" => FireRifleDir(new Vector2(0, 1)),

            // Parry (shared)
            "activate_parry" => _pawn.ActivateParry() ? BtStatus.Success : BtStatus.Failure,

            _ => BtStatus.Failure
        };
    }

    // ── Hook helpers ──

    private BtStatus LaunchHook(Vector2 dir)
    {
        if (_pawn.Role != PawnRole.Grappler) return BtStatus.Failure;
        if (_pawn.HookLockoutTicks > 0) return BtStatus.Failure;
        return _pawn.Hook.Launch(dir) ? BtStatus.Success : BtStatus.Failure;
    }

    private BtStatus LaunchHookAt(Vector2 target)
    {
        var dir = (target - _pawn.Position).Normalized();
        return LaunchHook(dir);
    }

    // ── Fist helpers ──

    private BtStatus ExecutePunch()
    {
        if (_pawn.Role != PawnRole.Grappler) return BtStatus.Failure;
        int lockoutBefore = _pawn.FistLockoutTicks;
        var hit = BeaconPhysics.ExecuteFistPunch(_pawn, _enemies);
        if (hit != null)
        {
            OnFistHit?.Invoke(hit);
            OnDamageDealt?.Invoke(_pawn.TeamIndex, Pawn.FistDamage);
        }
        else if (_pawn.FistLockoutTicks > lockoutBefore)
        {
            // Parry happened inside ExecuteFistPunch
            OnParrySuccess?.Invoke(1 - _pawn.TeamIndex);
        }
        return hit != null ? BtStatus.Success : BtStatus.Failure;
    }

    private BtStatus ExecutePunchDir(float dirX)
    {
        // Only hits enemies in the given direction
        if (_pawn.Role != PawnRole.Grappler) return BtStatus.Failure;
        if (_pawn.FistCooldown > 0 || _pawn.FistLockoutTicks > 0) return BtStatus.Failure;

        Pawn? bestTarget = null;
        float bestDist = Pawn.FistRange;

        foreach (var enemy in _enemies)
        {
            if (enemy.IsDead) continue;
            float dx = enemy.Position.X - _pawn.Position.X;
            if ((dirX > 0 && dx < 0) || (dirX < 0 && dx > 0)) continue;
            float dist = _pawn.Position.DistanceTo(enemy.Position);
            if (dist < bestDist) { bestDist = dist; bestTarget = enemy; }
        }

        if (bestTarget == null) return BtStatus.Failure;

        if (bestTarget.IsParryActive)
        {
            _pawn.FistLockoutTicks = BeaconPhysics.ParryLockoutDuration;
            _pawn.FistCooldown = Pawn.FistCooldownMax;
            OnParrySuccess?.Invoke(bestTarget.TeamIndex);
            return BtStatus.Failure;
        }

        float actualDmg = bestTarget.TakeDamage(Pawn.FistDamage);
        Vector2 knockDir = (bestTarget.Position - _pawn.Position).Normalized();
        bestTarget.ApplyKnockback(knockDir, Pawn.FistKnockback);
        _pawn.FistCooldown = Pawn.FistCooldownMax;
        OnFistHit?.Invoke(bestTarget);
        OnDamageDealt?.Invoke(_pawn.TeamIndex, actualDmg);
        return BtStatus.Success;
    }

    // ── Pistol helpers ──

    private BtStatus FirePistol(Vector2 dir)
    {
        if (_pawn.Role != PawnRole.Gunner) return BtStatus.Failure;
        if (_pawn.PistolCooldown > 0 || _pawn.PistolLockoutTicks > 0) return BtStatus.Failure;

        var velocity = dir * Pawn.PistolSpeed;
        var proj = new Projectile(
            _pawn.Position, velocity,
            BeaconPhysics.Gravity, Pawn.PistolDamage, Pawn.PistolKnockback,
            _pawn.TeamIndex, _pawn.PawnIndex, Pawn.PistolLifetime);
        proj.WeaponType = "pistol";
        Projectiles.Add(proj);
        _pawn.PistolCooldown = Pawn.PistolCooldownMax;
        OnPistolFired?.Invoke(_pawn.Position, velocity);
        return BtStatus.Success;
    }

    // Simple deterministic hash for pistol spread (varies per tick + pawn)
    private static float PistolSpreadAngle(int tick, int pawnIdx)
    {
        // Hash to get a value in [-5°, +5°] that varies per shot
        uint h = (uint)(tick * 2654435761 + pawnIdx * 40503);
        h ^= h >> 16;
        float norm = (h % 10001) / 10000f; // 0..1
        return (norm - 0.5f) * 2f * 5f * Mathf.Pi / 180f; // ±5° in radians
    }

    private BtStatus FirePistolAt(Vector2 target)
    {
        var dir = (target - _pawn.Position).Normalized();
        // Add slight upward arc for ballistic lob
        dir = new Vector2(dir.X, dir.Y - 0.15f).Normalized();
        // Apply ±5° inaccuracy spread
        float spreadRad = PistolSpreadAngle(_tick, _pawn.PawnIndex);
        float cos = Mathf.Cos(spreadRad);
        float sin = Mathf.Sin(spreadRad);
        dir = new Vector2(dir.X * cos - dir.Y * sin, dir.X * sin + dir.Y * cos);
        return FirePistol(dir);
    }

    // ── Rifle helpers ──

    private BtStatus FireRifleDir(Vector2 dir)
    {
        if (_pawn.Role != PawnRole.Gunner) return BtStatus.Failure;
        if (_pawn.RifleChargeTicks > 0) return BtStatus.Success; // already charging — report success
        if (_pawn.RifleCooldown > 0 || _pawn.RifleLockoutTicks > 0) return BtStatus.Failure;

        // Start charge — aim direction locked in, shot fires after delay
        _pawn.RifleChargeTicks = Pawn.RifleChargeTime;
        _pawn.RifleChargeDir = dir.Normalized();
        _pawn.RifleCooldown = Pawn.RifleCooldownMax; // cooldown starts at charge
        return BtStatus.Success;
    }

    private BtStatus FireRifleAt(Vector2 target)
    {
        var dir = (target - _pawn.Position).Normalized();
        return FireRifleDir(dir);
    }

    private BtStatus FireRifleWallBounce()
    {
        // Attempt to find a wall angle that ricochets toward nearest enemy
        if (_nearestEnemy == null) return BtStatus.Failure;

        // Simple heuristic: shoot at nearest wall and hope for ricochet
        // Shoot toward whichever wall the enemy is behind
        float dirX = _nearestEnemy.Position.X > _pawn.Position.X ? 1f : -1f;
        float dirY = _nearestEnemy.Position.Y > _pawn.Position.Y ? 0.3f : -0.3f;
        return FireRifleDir(new Vector2(dirX, dirY).Normalized());
    }

    // ── Movement helpers ──

    private BtStatus ApplyMove(float dirX)
    {
        float force = _pawn.IsGrounded ? BeaconPhysics.MoveForce : BeaconPhysics.AirMoveForce;
        _pawn.Velocity += new Vector2(dirX * force * BeaconPhysics.FixedDt, 0);
        return BtStatus.Success;
    }

    private BtStatus MoveToward(Vector2 target)
    {
        float dirX = target.X > _pawn.Position.X ? 1f : -1f;
        // Auto-jump when target is significantly above and we're close horizontally
        float dy = _pawn.Position.Y - target.Y; // positive = target is above (Y-down)
        float dx = Mathf.Abs(_pawn.Position.X - target.X);
        if (dy > 50f && dx < 200f && _pawn.IsGrounded)
            _pawn.Velocity = new Vector2(_pawn.Velocity.X, BeaconPhysics.JumpImpulse);
        return ApplyMove(dirX);
    }

    private BtStatus MoveTowardNearestUnowned()
    {
        int myTeam = _pawn.TeamIndex + 1;
        Beacon? nearest = null;
        float bestDist = float.MaxValue;
        foreach (var b in _beacons)
        {
            if (b.OwnerTeam == myTeam) continue;
            float d = _pawn.Position.DistanceTo(b.Zone.Center);
            if (d < bestDist) { bestDist = d; nearest = b; }
        }
        return nearest != null ? MoveToward(nearest.Zone.Center) : BtStatus.Failure;
    }

    private BtStatus MoveTowardNearestContested()
    {
        Beacon? nearest = null;
        float bestDist = float.MaxValue;
        foreach (var b in _beacons)
        {
            if (!b.IsContested) continue;
            float d = _pawn.Position.DistanceTo(b.Zone.Center);
            if (d < bestDist) { bestDist = d; nearest = b; }
        }
        return nearest != null ? MoveToward(nearest.Zone.Center) : BtStatus.Failure;
    }

    private BtStatus MoveTowardNearestPickup()
    {
        if (_match == null) return BtStatus.Failure;
        var pickups = _arena.Modifiers.Pickups;
        Vector2? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < pickups.Count && i < _match.PickupActive.Length; i++)
        {
            if (!_match.PickupActive[i]) continue;
            var pos = new Vector2(pickups[i].X, pickups[i].Y);
            float d = _pawn.Position.DistanceTo(pos);
            if (d < bestDist) { bestDist = d; best = pos; }
        }
        return best.HasValue ? MoveToward(best.Value) : BtStatus.Failure;
    }

    /// <summary>Move toward the nearest edge of the hazard zone we're standing in.</summary>
    private BtStatus MoveOffHazard()
    {
        float px = _pawn.Position.X;
        foreach (var hz in _arena.Modifiers.HazardZones)
        {
            if (px >= hz.X && px <= hz.X + hz.Width)
            {
                float distToLeft = px - hz.X;
                float distToRight = (hz.X + hz.Width) - px;
                return ApplyMove(distToLeft < distToRight ? -1f : 1f);
            }
        }
        return BtStatus.Failure;
    }

    private BtStatus Jump()
    {
        if (!_pawn.IsGrounded) return BtStatus.Failure;
        _pawn.Velocity = new Vector2(_pawn.Velocity.X, BeaconPhysics.JumpImpulse);
        return BtStatus.Success;
    }

    private static BtStatus Do(Action a) { a(); return BtStatus.Success; }

    private static float BeaconOwnerPerspective(int ownerTeam, int myTeam, int enemyTeam)
    {
        if (ownerTeam == 0) return 0f;
        if (ownerTeam == myTeam) return 1f;
        return 2f;
    }

    private static int CountInBeacon(IEnumerable<Pawn> pawns, Beacon b) =>
        pawns.Count(p => b.Contains(p.Position));

    private float NearestUnownedBeaconDist(int myTeam)
    {
        float best = float.MaxValue;
        foreach (var b in _beacons)
        {
            if (b.OwnerTeam == myTeam) continue;
            float d = _pawn.Position.DistanceTo(b.Zone.Center);
            if (d < best) best = d;
        }
        return best < float.MaxValue ? best : 0f;
    }

    private float NearestUnownedBeaconDirX(int myTeam)
    {
        float bestDist = float.MaxValue;
        float dirX = 0f;
        foreach (var b in _beacons)
        {
            if (b.OwnerTeam == myTeam) continue;
            float d = _pawn.Position.DistanceTo(b.Zone.Center);
            if (d < bestDist)
            {
                bestDist = d;
                dirX = b.Zone.Center.X > _pawn.Position.X ? 1f : -1f;
            }
        }
        return dirX;
    }

    private Pawn? NearestAlly()
    {
        Pawn? nearest = null;
        float best = float.MaxValue;
        foreach (var a in _allies)
        {
            if (a.IsDead) continue;
            float d = _pawn.Position.DistanceTo(a.Position);
            if (d < best) { best = d; nearest = a; }
        }
        return nearest;
    }
}
