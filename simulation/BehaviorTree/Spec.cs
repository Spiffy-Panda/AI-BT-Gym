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

    // Chain lengths
    public static readonly VarRef LeftChain = new("left_chain");
    public static readonly VarRef RightChain = new("right_chain");

    // Direction
    public static readonly VarRef OpponentDirX = new("opponent_dir_x");
    public static readonly VarRef OpponentDirY = new("opponent_dir_y");

    // ── Map awareness: static geometry ──
    public static readonly VarRef ArenaWidth = new("arena_width");
    public static readonly VarRef ArenaHeight = new("arena_height");
    public static readonly VarRef PlatformCount = new("platform_count");
    public static readonly VarRef HazardCount = new("hazard_count");
    public static readonly VarRef HasShrink = new("has_shrink");
    public static readonly VarRef HasCeiling = new("has_ceiling");
    public static readonly VarRef HasBumpers = new("has_bumpers");
    public static readonly VarRef HasFriction = new("has_friction");
    public static readonly VarRef WallCount = new("wall_count");
    public static readonly VarRef PickupCount = new("pickup_count");

    // ── Map awareness: dynamic state ──
    public static readonly VarRef OnPlatform = new("on_platform");
    public static readonly VarRef OnHazard = new("on_hazard");
    public static readonly VarRef InFrictionZone = new("in_friction_zone");
    public static readonly VarRef NearestPlatformDist = new("nearest_platform_dist");
    public static readonly VarRef ArenaLeft = new("arena_left");
    public static readonly VarRef ArenaRight = new("arena_right");

    // Indexed map variables — use VarRef("pickup_0_active") etc. for specific indices
    public static VarRef PickupActive(int i) => new($"pickup_{i}_active");
    public static VarRef PickupX(int i) => new($"pickup_{i}_x");
    public static VarRef PickupY(int i) => new($"pickup_{i}_y");
    public static VarRef PickupDist(int i) => new($"pickup_{i}_dist");
    public static VarRef WallHp(int i) => new($"wall_{i}_hp");
    public static VarRef WallExists(int i) => new($"wall_{i}_exists");
    public static VarRef PlatformX(int i) => new($"platform_{i}_x");
    public static VarRef PlatformY(int i) => new($"platform_{i}_y");
    public static VarRef PlatformW(int i) => new($"platform_{i}_w");

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

    // ── Own fist anchored (locked in free space) ──
    public static readonly ISpec LeftAnchored = Var.LeftAttached.Eq(1);
    public static readonly ISpec RightAnchored = Var.RightAttached.Eq(1);

    // ── Opponent fist state ──
    public static readonly ISpec OppLeftExtending = Var.OppLeftState.Eq(1);
    public static readonly ISpec OppLeftLocked = Var.OppLeftState.Eq(2);
    public static readonly ISpec OppLeftRetracting = Var.OppLeftState.Eq(3);
    public static readonly ISpec OppRightExtending = Var.OppRightState.Eq(1);
    public static readonly ISpec OppRightLocked = Var.OppRightState.Eq(2);
    public static readonly ISpec OppRightRetracting = Var.OppRightState.Eq(3);

    // ── Chain extension ──
    public static ISpec LeftChainOver(float len) => Var.LeftChain.Gt(len);
    public static ISpec RightChainOver(float len) => Var.RightChain.Gt(len);

    // ── Map awareness ──
    public static readonly ISpec HasPlatforms = Var.PlatformCount.Gt(0);
    public static readonly ISpec HasHazards = Var.HazardCount.Gt(0);
    public static readonly ISpec HasPickups = Var.PickupCount.Gt(0);
    public static readonly ISpec HasWalls = Var.WallCount.Gt(0);
    public static readonly ISpec ArenaShrinking = Var.HasShrink.Eq(1);
    public static readonly ISpec HasDippedCeiling = Var.HasCeiling.Eq(1);
    public static readonly ISpec HasCornerBumpers = Var.HasBumpers.Eq(1);
    public static readonly ISpec HasStickyWalls = Var.HasFriction.Eq(1);
    public static readonly ISpec StandingOnPlatform = Var.OnPlatform.Eq(1);
    public static readonly ISpec StandingOnHazard = Var.OnHazard.Eq(1);
    public static readonly ISpec InWallFriction = Var.InFrictionZone.Eq(1);
    public static ISpec PlatformNearby(float dist) => Var.NearestPlatformDist.Lt(dist);
    public static ISpec PickupAvailable(int i = 0) => Var.PickupActive(i).Eq(1);
    public static ISpec PickupClose(int i, float dist) => Var.PickupDist(i).Lt(dist);
    public static ISpec WallStillStanding(int i = 0) => Var.WallExists(i).Eq(1);
}
