// ─────────────────────────────────────────────────────────────────────────────
// FighterBtContext.cs — Bridges BT leaf nodes to the simulation
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation;

public class FighterBtContext : IBtContext
{
    private readonly Fighter _fighter;
    private readonly Fighter _opponent;
    private readonly Arena _arena;
    private readonly int _tick;
    private readonly Match? _match;

    public int CurrentTick => _tick;

    /// <summary>Optional callback invoked with the action name when an action succeeds.</summary>
    public Action<string>? OnActionExecuted { get; set; }

    /// <summary>Legacy constructor (no match reference — map feature variables return 0).</summary>
    public FighterBtContext(Fighter fighter, Fighter opponent, Arena arena, int tick)
        : this(fighter, opponent, arena, tick, null) { }

    public FighterBtContext(Fighter fighter, Fighter opponent, Arena arena, int tick, Match? match)
    {
        _fighter = fighter;
        _opponent = opponent;
        _arena = arena;
        _tick = tick;
        _match = match;
    }

    // ── Variable resolution ──

    public float ResolveVariable(string name) => name switch
    {
        "health" => _fighter.Health,
        "opponent_health" => _opponent.Health,
        "distance_to_opponent" => _fighter.Position.DistanceTo(_opponent.Position),
        "pos_x" => _fighter.Position.X,
        "pos_y" => _fighter.Position.Y,
        "vel_x" => _fighter.Velocity.X,
        "vel_y" => _fighter.Velocity.Y,
        "opponent_pos_x" => _opponent.Position.X,
        "opponent_pos_y" => _opponent.Position.Y,
        "is_grounded" => _fighter.IsGrounded ? 1f : 0f,

        // Fist states (0=Retracted, 1=Extending, 2=Locked, 3=Retracting)
        "left_state" => (float)_fighter.LeftFist.ChainState,
        "right_state" => (float)_fighter.RightFist.ChainState,
        "left_attached" => _fighter.LeftFist.IsAttachedToWorld ? 1f : 0f,
        "right_attached" => _fighter.RightFist.IsAttachedToWorld ? 1f : 0f,
        "left_retracted" => _fighter.LeftFist.ChainState == FistChainState.Retracted ? 1f : 0f,
        "right_retracted" => _fighter.RightFist.ChainState == FistChainState.Retracted ? 1f : 0f,

        // Chain lengths
        "left_chain" => _fighter.LeftFist.ChainLength,
        "right_chain" => _fighter.RightFist.ChainLength,

        // Opponent fists
        "opp_left_state" => (float)_opponent.LeftFist.ChainState,
        "opp_right_state" => (float)_opponent.RightFist.ChainState,

        // Direction to opponent (normalized): positive = opponent is to the right
        "opponent_dir_x" => _opponent.Position.X > _fighter.Position.X ? 1f : -1f,
        "opponent_dir_y" => _opponent.Position.Y > _fighter.Position.Y ? 1f : -1f,

        // ── Map awareness: static geometry ──
        "arena_width" => _arena.Config.Width,
        "arena_height" => _arena.Config.Height,
        "platform_count" => _arena.Config.Platforms.Count,
        "hazard_count" => _arena.Config.HazardZones.Count,
        "has_shrink" => _arena.Config.Shrink != null ? 1f : 0f,
        "has_ceiling" => _arena.Config.Ceiling != null ? 1f : 0f,
        "has_bumpers" => _arena.Config.CornerBumpers.Count > 0 ? 1f : 0f,
        "has_friction" => _arena.Config.WallFrictionZones.Count > 0 ? 1f : 0f,
        "wall_count" => _arena.Config.DestructibleWalls.Count,
        "pickup_count" => _arena.Config.Pickups.Count,

        // ── Map awareness: dynamic state ──
        "on_platform" => _arena.IsOnPlatform(_fighter.Position, _fighter.BodyRadius, _fighter.Velocity) ? 1f : 0f,
        "on_hazard" => (_arena.IsOnGround(_fighter.Position, _fighter.BodyRadius) && _arena.IsInHazardZone(_fighter.Position)) ? 1f : 0f,
        "in_friction_zone" => _arena.IsInWallFrictionZone(_fighter.Position, _fighter.BodyRadius) ? 1f : 0f,

        // Nearest platform
        "nearest_platform_dist" => _arena.Config.Platforms.Count > 0
            ? _arena.DistanceToNearestPlatform(_fighter.Position) : 0f,

        // Effective arena bounds (accounts for shrink)
        "arena_left" => _match?.EffectiveLeft ?? _arena.Bounds.Position.X,
        "arena_right" => _match?.EffectiveRight ?? _arena.Bounds.End.X,

        // Indexed variables handled below
        _ => ResolveIndexedVariable(name)
    };

    private float ResolveIndexedVariable(string name)
    {
        // Platform indexed: platform_N_x, platform_N_y, platform_N_w
        if (name.StartsWith("platform_") && name.Length > 10)
        {
            var parts = name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int idx)
                && idx >= 0 && idx < _arena.Config.Platforms.Count)
            {
                var plat = _arena.Config.Platforms[idx];
                return parts[2] switch
                {
                    "x" => plat.X,
                    "y" => plat.Y,
                    "w" => plat.Width,
                    _ => 0f
                };
            }
        }

        // Destructible wall indexed: wall_N_hp, wall_N_exists
        if (name.StartsWith("wall_") && _match != null)
        {
            var parts = name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int idx)
                && idx >= 0 && idx < _match.DestructibleWallHp.Length)
            {
                return parts[2] switch
                {
                    "hp" => _match.DestructibleWallHp[idx],
                    "exists" => _match.DestructibleWallExists[idx] ? 1f : 0f,
                    _ => 0f
                };
            }
        }

        // Pickup indexed: pickup_N_active, pickup_N_x, pickup_N_y, pickup_N_dist
        if (name.StartsWith("pickup_") && _match != null)
        {
            var parts = name.Split('_');
            if (parts.Length == 3 && int.TryParse(parts[1], out int idx)
                && idx >= 0 && idx < _arena.Config.Pickups.Count)
            {
                var pickup = _arena.Config.Pickups[idx];
                return parts[2] switch
                {
                    "active" => _match.PickupActive[idx] ? 1f : 0f,
                    "x" => pickup.X,
                    "y" => pickup.Y,
                    "dist" => _fighter.Position.DistanceTo(new Vector2(pickup.X, pickup.Y)),
                    _ => 0f
                };
            }
        }

        return 0f;
    }

    // ── Action execution ──

    public BtStatus ExecuteAction(string action)
    {
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
            // Launch fist in specific directions
            "launch_left_at_opponent" => LaunchAt(_fighter.LeftFist, _opponent.Position),
            "launch_right_at_opponent" => LaunchAt(_fighter.RightFist, _opponent.Position),
            "launch_left_up" => Launch(_fighter.LeftFist, new Vector2(0, -1)),
            "launch_right_up" => Launch(_fighter.RightFist, new Vector2(0, -1)),
            "launch_left_upleft" => Launch(_fighter.LeftFist, new Vector2(-1, -1).Normalized()),
            "launch_right_upright" => Launch(_fighter.RightFist, new Vector2(1, -1).Normalized()),
            "launch_left_down" => Launch(_fighter.LeftFist, new Vector2(0, 1)),
            "launch_right_down" => Launch(_fighter.RightFist, new Vector2(0, 1)),

            // State changes
            "lock_left" => _fighter.LeftFist.Lock() ? BtStatus.Success : BtStatus.Failure,
            "lock_right" => _fighter.RightFist.Lock() ? BtStatus.Success : BtStatus.Failure,
            "retract_left" => _fighter.LeftFist.Retract() ? BtStatus.Success : BtStatus.Failure,
            "retract_right" => _fighter.RightFist.Retract() ? BtStatus.Success : BtStatus.Failure,
            "detach_left" => Do(() => _fighter.LeftFist.Detach()),
            "detach_right" => Do(() => _fighter.RightFist.Detach()),

            // Movement
            "move_left" => ApplyMove(-1f),
            "move_right" => ApplyMove(1f),
            "move_toward_opponent" => ApplyMove(_opponent.Position.X > _fighter.Position.X ? 1f : -1f),
            "move_away_from_opponent" => ApplyMove(_opponent.Position.X > _fighter.Position.X ? -1f : 1f),
            "jump" => Jump(),

            _ => BtStatus.Failure
        };
    }

    // ── Helpers ──

    private BtStatus Launch(Fist fist, Vector2 dir) =>
        fist.Launch(dir) ? BtStatus.Success : BtStatus.Failure;

    private BtStatus LaunchAt(Fist fist, Vector2 target)
    {
        var dir = (target - _fighter.Position).Normalized();
        return fist.Launch(dir) ? BtStatus.Success : BtStatus.Failure;
    }

    private BtStatus ApplyMove(float dirX)
    {
        float force = _fighter.IsGrounded ? SimPhysics.MoveForce : SimPhysics.AirMoveForce;
        _fighter.Velocity += new Vector2(dirX * force * SimPhysics.FixedDt, 0);
        return BtStatus.Success;
    }

    private BtStatus Jump()
    {
        if (!_fighter.IsGrounded) return BtStatus.Failure;
        _fighter.Velocity = new Vector2(_fighter.Velocity.X, SimPhysics.JumpImpulse);
        return BtStatus.Success;
    }

    private static BtStatus Do(Action a) { a(); return BtStatus.Success; }
}
