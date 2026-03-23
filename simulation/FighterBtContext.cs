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

    public int CurrentTick => _tick;

    public FighterBtContext(Fighter fighter, Fighter opponent, Arena arena, int tick)
    {
        _fighter = fighter;
        _opponent = opponent;
        _arena = arena;
        _tick = tick;
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

        // Opponent fists
        "opp_left_state" => (float)_opponent.LeftFist.ChainState,
        "opp_right_state" => (float)_opponent.RightFist.ChainState,

        // Proximity
        "near_wall_left" => _arena.IsNearWallLeft(_fighter.Position) ? 1f : 0f,
        "near_wall_right" => _arena.IsNearWallRight(_fighter.Position) ? 1f : 0f,
        "near_ceiling" => _arena.IsNearCeiling(_fighter.Position) ? 1f : 0f,

        // Direction to opponent (normalized): positive = opponent is to the right
        "opponent_dir_x" => _opponent.Position.X > _fighter.Position.X ? 1f : -1f,
        "opponent_dir_y" => _opponent.Position.Y > _fighter.Position.Y ? 1f : -1f,

        _ => 0f
    };

    // ── Action execution ──

    public BtStatus ExecuteAction(string action)
    {
        var parts = action.Split(' ', 2);
        var cmd = parts[0].ToLowerInvariant();

        return cmd switch
        {
            // Launch fist in specific directions
            "launch_left_at_opponent" => LaunchAt(_fighter.LeftFist, _opponent.Position),
            "launch_right_at_opponent" => LaunchAt(_fighter.RightFist, _opponent.Position),
            "launch_left_up" => Launch(_fighter.LeftFist, new Vector2(0, -1)),
            "launch_right_up" => Launch(_fighter.RightFist, new Vector2(0, -1)),
            "launch_left_upleft" => Launch(_fighter.LeftFist, new Vector2(-1, -1).Normalized()),
            "launch_right_upright" => Launch(_fighter.RightFist, new Vector2(1, -1).Normalized()),
            "launch_left_wall" => LaunchToNearestWall(_fighter.LeftFist),
            "launch_right_wall" => LaunchToNearestWall(_fighter.RightFist),
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

    private BtStatus LaunchToNearestWall(Fist fist)
    {
        // Launch toward the nearest wall
        float distLeft = _fighter.Position.X - _arena.Bounds.Position.X;
        float distRight = _arena.Bounds.End.X - _fighter.Position.X;
        float distCeiling = _fighter.Position.Y - _arena.Bounds.Position.Y;

        Vector2 dir;
        if (distCeiling < distLeft && distCeiling < distRight)
            dir = new Vector2(0, -1);
        else if (distLeft < distRight)
            dir = new Vector2(-1, 0);
        else
            dir = new Vector2(1, 0);

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
