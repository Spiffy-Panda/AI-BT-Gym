// ─────────────────────────────────────────────────────────────────────────────
// Spec.cs — Specification Pattern for type-safe BT condition construction
// ─────────────────────────────────────────────────────────────────────────────
//
// Specs produce the same condition strings that BtRunner.EvalCondition parses.
// They are evaluated at tree-construction time, not at tick time — zero runtime
// overhead. Use `Var.*` for IntelliSense-discoverable variable references.

using System.Globalization;

namespace AiBtGym.BehaviorTree;

/// <summary>A specification that produces a condition expression string.</summary>
public interface ISpec
{
    string Expr { get; }
}

/// <summary>Strongly-typed reference to a context variable.</summary>
public readonly record struct VarRef(string Name) : ISpec
{
    public string Expr => Name;

    public ISpec Lt(float v) => new CompareSpec(Name, "<", v);
    public ISpec Le(float v) => new CompareSpec(Name, "<=", v);
    public ISpec Gt(float v) => new CompareSpec(Name, ">", v);
    public ISpec Ge(float v) => new CompareSpec(Name, ">=", v);
    public ISpec Eq(float v) => new CompareSpec(Name, "==", v);
    public ISpec Ne(float v) => new CompareSpec(Name, "!=", v);

    public ExprRef Plus(float v) => new($"{Name} + {Fmt(v)}");
    public ExprRef Minus(float v) => new($"{Name} - {Fmt(v)}");
    public ExprRef Times(float v) => new($"{Name} * {Fmt(v)}");
    public ExprRef Div(float v) => new($"{Name} / {Fmt(v)}");

    private static string Fmt(float v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A computed expression (e.g. "health + 10") that supports comparisons.</summary>
public readonly record struct ExprRef(string Expression) : ISpec
{
    public string Expr => Expression;

    public ISpec Lt(float v) => new CompareSpec(Expression, "<", v);
    public ISpec Le(float v) => new CompareSpec(Expression, "<=", v);
    public ISpec Gt(float v) => new CompareSpec(Expression, ">", v);
    public ISpec Ge(float v) => new CompareSpec(Expression, ">=", v);
    public ISpec Eq(float v) => new CompareSpec(Expression, "==", v);
    public ISpec Ne(float v) => new CompareSpec(Expression, "!=", v);
}

/// <summary>A comparison like "distance_to_opponent &lt; 180".</summary>
public readonly record struct CompareSpec(string Lhs, string Op, float Rhs) : ISpec
{
    public string Expr =>
        $"{Lhs} {Op} {Rhs.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A literal condition ("always", "never").</summary>
public readonly record struct LiteralSpec(string Value) : ISpec
{
    public string Expr => Value;
}

/// <summary>All known context variables as strongly-typed references.</summary>
public static class Var
{
    // Fighter state
    public static readonly VarRef Health = new("health");
    public static readonly VarRef OpponentHealth = new("opponent_health");
    public static readonly VarRef Distance = new("distance_to_opponent");
    public static readonly VarRef PosX = new("pos_x");
    public static readonly VarRef PosY = new("pos_y");
    public static readonly VarRef VelX = new("vel_x");
    public static readonly VarRef VelY = new("vel_y");
    public static readonly VarRef OpponentPosX = new("opponent_pos_x");
    public static readonly VarRef OpponentPosY = new("opponent_pos_y");
    public static readonly VarRef IsGrounded = new("is_grounded");

    // Fist states (0=Retracted, 1=Extending, 2=Locked, 3=Retracting)
    public static readonly VarRef LeftState = new("left_state");
    public static readonly VarRef RightState = new("right_state");
    public static readonly VarRef LeftAttached = new("left_attached");
    public static readonly VarRef RightAttached = new("right_attached");
    public static readonly VarRef LeftRetracted = new("left_retracted");
    public static readonly VarRef RightRetracted = new("right_retracted");

    // Opponent fists
    public static readonly VarRef OppLeftState = new("opp_left_state");
    public static readonly VarRef OppRightState = new("opp_right_state");

    // Arena proximity
    public static readonly VarRef NearWallLeft = new("near_wall_left");
    public static readonly VarRef NearWallRight = new("near_wall_right");
    public static readonly VarRef NearCeiling = new("near_ceiling");

    // Direction
    public static readonly VarRef OpponentDirX = new("opponent_dir_x");
    public static readonly VarRef OpponentDirY = new("opponent_dir_y");

    // Literals
    public static readonly ISpec Always = new LiteralSpec("always");
    public static readonly ISpec Never = new LiteralSpec("never");
}

/// <summary>
/// Registry of common, reusable conditions with semantic names.
/// Use via <c>using static AiBtGym.BehaviorTree.When;</c>.
/// </summary>
public static class When
{
    // ── Movement state ──
    public static readonly ISpec Grounded = Var.IsGrounded.Eq(1);
    public static readonly ISpec Airborne = Var.IsGrounded.Eq(0);
    public static readonly ISpec Descending = Var.VelY.Gt(0);
    public static readonly ISpec Ascending = Var.VelY.Lt(0);

    // ── Distance thresholds ──
    public static ISpec InRange(float range) => Var.Distance.Lt(range);
    public static ISpec OutOfRange(float range) => Var.Distance.Gt(range);
    public static readonly ISpec InMeleeRange = Var.Distance.Lt(150);
    public static readonly ISpec InAttackRange = Var.Distance.Lt(220);
    public static readonly ISpec InPokeRange = Var.Distance.Lt(260);
    public static readonly ISpec Far = Var.Distance.Gt(280);

    // ── Own fist readiness ──
    public static readonly ISpec LeftReady = Var.LeftRetracted.Eq(1);
    public static readonly ISpec RightReady = Var.RightRetracted.Eq(1);

    // ── Own fist state ──
    public static readonly ISpec LeftExtending = Var.LeftState.Eq(1);
    public static readonly ISpec LeftLocked = Var.LeftState.Eq(2);
    public static readonly ISpec LeftRetracting = Var.LeftState.Eq(3);
    public static readonly ISpec RightExtending = Var.RightState.Eq(1);
    public static readonly ISpec RightLocked = Var.RightState.Eq(2);
    public static readonly ISpec RightRetracting = Var.RightState.Eq(3);

    // ── Own fist anchored (attached to world surface) ──
    public static readonly ISpec LeftAnchored = Var.LeftAttached.Eq(1);
    public static readonly ISpec RightAnchored = Var.RightAttached.Eq(1);

    // ── Opponent fist state ──
    public static readonly ISpec OppLeftExtending = Var.OppLeftState.Eq(1);
    public static readonly ISpec OppLeftLocked = Var.OppLeftState.Eq(2);
    public static readonly ISpec OppLeftRetracting = Var.OppLeftState.Eq(3);
    public static readonly ISpec OppRightExtending = Var.OppRightState.Eq(1);
    public static readonly ISpec OppRightLocked = Var.OppRightState.Eq(2);
    public static readonly ISpec OppRightRetracting = Var.OppRightState.Eq(3);

    // ── Arena proximity ──
    public static readonly ISpec AtLeftWall = Var.NearWallLeft.Eq(1);
    public static readonly ISpec AtRightWall = Var.NearWallRight.Eq(1);
    public static readonly ISpec AtCeiling = Var.NearCeiling.Eq(1);
}
