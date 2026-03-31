// ─────────────────────────────────────────────────────────────────────────────
// MapTestTree.cs — Adaptive BT that reacts to every arena configuration
// ─────────────────────────────────────────────────────────────────────────────
//
// Architecture: a Parallel node runs two children every tick:
//   1. AttackLayer — always fires fists when in range (never starved)
//   2. MovementLayer — picks the best movement based on active map features
//
// This ensures feature-specific movement never crowds out attacking.

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;

namespace AiBtGym.Godot;

public static class MapTestTree
{
    public static readonly string Name = "MapTester";

    public static List<BtNode> Build() =>
    [
        Par(ParallelPolicy.RequireOne,
            AttackLayer(),
            MovementLayer()
        )
    ];

    // ═════════════════════════════════════════════════════════════════════
    //  ATTACK LAYER — runs every tick, independent of movement
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Always-on attack logic. Fires fists at opponent when in range,
    /// manages grapple state (lock/retract extending fists).
    /// </summary>
    private static BtNode AttackLayer() =>
        Sel("attack",
            // Close range: punch with available fist
            Seq(Cond(InRange(220)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Mid range: launch fist at opponent (may hit or become grapple anchor)
            Seq(Cond(OutOfRange(220)), Cond(InRange(350)),
                Sel(
                    Seq(Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // Manage extending fists: lock for grapple anchor when far
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),
            Seq(Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(90)), Act("lock_left")),

            // While grappling with right, also attack with left
            Seq(Cond(RightAnchored), Cond(InRange(250)), Cond(LeftReady),
                Act("launch_left_at_opponent")),

            // Pull toward grapple anchor
            Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
            Seq(Cond(LeftAnchored), Cond(LeftLocked), Act("retract_left"))
        );

    // ═════════════════════════════════════════════════════════════════════
    //  MOVEMENT LAYER — picks best movement each tick
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Movement decision tree. Feature sub-strategies only control WHERE
    /// the fighter moves, not whether it attacks (attack layer handles that).
    /// </summary>
    private static BtNode MovementLayer() =>
        Sel("movement",
            HazardEscape(),       // P1: get off damage zones
            PickupDetour(),       // P2: grab health when hurt
            PlatformMovement(),   // P3: move toward/onto platforms
            ShrinkMovement(),     // P4: stay in shrinking bounds
            CeilingApproach(),    // P5: use low ceiling for grapple approach
            DefaultMovement()     // P6: walk toward opponent
        );

    // ── Movement sub-strategies ──

    private static BtNode HazardEscape() =>
        Seq("hazard_escape",
            Cond(StandingOnHazard),
            Sel(
                Seq(Cond(Grounded), Act("jump")),
                Seq(Cond("pos_x < 750"), Act("move_right")),
                Act("move_left")
            )
        );

    private static BtNode PickupDetour() =>
        Seq("pickup_detour",
            Cond(HasPickups),
            Cond(Health.Lt(80)),
            Cond(PickupAvailable(0)),
            Cond(PickupClose(0, 400)),
            Sel(
                Seq(Cond(Grounded), Cond(PickupDist(0).Gt(80)), Act("jump")),
                Seq(Cond("pickup_0_x - pos_x > 10"), Act("move_right")),
                Seq(Cond("pos_x - pickup_0_x > 10"), Act("move_left")),
                Act("move_toward_opponent")
            )
        );

    /// <summary>
    /// Platform movement: walk under the platform, jump up, drift to land.
    /// Only controls movement — attacks happen via AttackLayer.
    /// </summary>
    private static BtNode PlatformMovement() =>
        Seq("platform_move",
            Cond(HasPlatforms),
            Sel(
                // On platform — move toward opponent along it (attacks handled by attack layer)
                Seq(Cond(StandingOnPlatform), Act("move_toward_opponent")),

                // Grounded near platform — jump up
                Seq(Cond(Grounded), Cond(PlatformNearby(350)), Act("jump")),

                // Airborne near platform — drift to land on it
                Seq(Cond(Airborne), Cond(PlatformNearby(200)),
                    Sel(
                        Seq(Cond("platform_0_x - pos_x > 20"), Act("move_right")),
                        Seq(Cond("pos_x - platform_0_x > 20"), Act("move_left"))
                    )
                ),

                // Walk toward platform
                Seq(Cond(Grounded),
                    Sel(
                        Seq(Cond("platform_0_x - pos_x > 30"), Act("move_right")),
                        Seq(Cond("pos_x - platform_0_x > 30"), Act("move_left"))
                    )
                )
            )
        );

    /// <summary>
    /// Shrink movement: stay inside effective bounds. No evasion — just
    /// avoid the walls and move toward opponent. The attack layer handles punching.
    /// </summary>
    private static BtNode ShrinkMovement() =>
        Seq("shrink_move",
            Cond(ArenaShrinking),
            Cond(ArenaLeft.Gt(50)), // bounds have actually shrunk
            Sel(
                // Too close to effective left wall
                Seq(Cond("pos_x - arena_left < 80"), Act("move_right")),
                // Too close to effective right wall
                Seq(Cond("arena_right - pos_x < 80"), Act("move_left")),
                // Inside bounds — just approach
                Act("move_toward_opponent")
            )
        );

    /// <summary>
    /// Dipped ceiling approach: when far from opponent on a dipped ceiling map,
    /// grapple to the low center ceiling for a fast swing approach.
    /// Only fires from ground in center zone — doesn't loop in the air.
    /// </summary>
    private static BtNode CeilingApproach() =>
        Seq("ceiling_approach",
            Cond(HasDippedCeiling),
            Cond(OutOfRange(300)),
            Cond(Grounded),
            // Must be in the center zone where ceiling is lowest
            Cond("pos_x > 400"),
            Cond("pos_x < 1100"),
            Sel(
                // Launch upward for ceiling grapple — attack layer handles lock/retract
                Seq(Cond(RightReady), Act("launch_right_up")),
                Act("move_toward_opponent")
            )
        );

    /// <summary>Default movement: walk toward opponent, jump when far.</summary>
    private static BtNode DefaultMovement() =>
        Sel("default_move",
            Seq(Cond(Grounded), Cond(OutOfRange(300)), Act("jump")),
            Act("move_toward_opponent")
        );
}
