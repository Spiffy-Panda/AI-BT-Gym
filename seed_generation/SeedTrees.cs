// ─────────────────────────────────────────────────────────────────────────────
// SeedTrees.cs — 6 competitive seed AIs for evolutionary tournament seeding
// Colors: Red, Green, Blue, Cyan, Yellow, Magenta
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;

namespace AiBtGym.Godot;

public static class SeedTrees
{
    public static readonly string[] Names =
    [
        "Red_CounterStriker",
        "Green_GrappleAssassin",
        "Blue_WallBouncer",
        "Cyan_ZoneController",
        "Yellow_DiveKicker",
        "Magenta_SwingSniper"
    ];

    public static readonly Color[] Colors =
    [
        new Color(0.9f, 0.15f, 0.15f),  // Red
        new Color(0.15f, 0.85f, 0.2f),  // Green
        new Color(0.2f, 0.35f, 0.95f),  // Blue
        new Color(0.1f, 0.9f, 0.9f),    // Cyan
        new Color(0.95f, 0.9f, 0.1f),   // Yellow
        new Color(0.9f, 0.15f, 0.85f)   // Magenta
    ];

    public static readonly List<BtNode>[] All =
    [
        RedCounterStriker(),
        GreenGrappleAssassin(),
        BlueWallBouncer(),
        CyanZoneController(),
        YellowDiveKicker(),
        MagentaSwingSniper()
    ];

    /// <summary>
    /// Red — CounterStriker: Maintains mid-range spacing, baits attacks, then
    /// punishes with quick counter-punches. Retreats when too close, advances
    /// when too far. Uses jump to dodge and reposition.
    /// </summary>
    public static List<BtNode> RedCounterStriker() =>
    [
        Sel(
            // Dodge: if opponent fist is extending toward us and we're close, jump away
            Seq(
                Cond(OppLeftExtending),
                Cond(InRange(200)),
                Cond(Grounded),
                Act("jump")
            ),
            Seq(
                Cond(OppRightExtending),
                Cond(InRange(200)),
                Cond(Grounded),
                Act("jump")
            ),

            // Counter-punch: if opponent's fist is retracting (whiffed), punish
            Seq(
                Cond(InAttackRange),
                Sel(
                    Cond(OppLeftRetracting),
                    Cond(OppRightRetracting)
                ),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Proactive poke at mid-range with one fist, keep other ready
            Seq(
                Cond(InRange(250)),
                Cond(OutOfRange(140)),
                Cond(LeftReady),
                Cond(RightReady),
                Act("launch_left_at_opponent")
            ),

            // Too close — back off
            Seq(
                Cond(InRange(100)),
                Act("move_away_from_opponent")
            ),

            // Close distance if too far
            Seq(
                Cond(Far),
                Act("move_toward_opponent")
            ),

            // Default: drift toward opponent
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Green — GrappleAssassin: Grapples to the ceiling, builds speed, releases
    /// at peak velocity for a diving strike. Alternates between ceiling anchors
    /// to stay aerial. Attacks with the free fist during dives.
    /// </summary>
    public static List<BtNode> GreenGrappleAssassin() =>
    [
        Sel(
            // Phase: Right locked to ceiling — swing and look for attack window
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    // Dive attack: release at downward velocity + strike with left
                    Seq(
                        Cond(VelY.Gt(50)),
                        Cond(InRange(250)),
                        Cond(LeftReady),
                        Act("launch_left_at_opponent")
                    ),
                    // Release swing at upward velocity to gain height
                    Seq(
                        Cond(VelY.Lt(-100)),
                        Act("retract_right")
                    ),
                    // Build momentum
                    Act("move_toward_opponent")
                )
            ),

            // Phase: Left locked to ceiling — mirror
            Seq(
                Cond(LeftAnchored),
                Cond(LeftLocked),
                Sel(
                    Seq(
                        Cond(VelY.Gt(50)),
                        Cond(InRange(250)),
                        Cond(RightReady),
                        Act("launch_right_at_opponent")
                    ),
                    Seq(
                        Cond(VelY.Lt(-100)),
                        Act("retract_left")
                    ),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists that attached to ceiling
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
            Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),

            // Launch to ceiling — alternate based on what's available
            Seq(Cond(RightReady), Act("launch_right_up")),
            Seq(Cond(LeftReady), Act("launch_left_up")),

            // Ground fallback: jump to get airborne
            Seq(
                Cond(Grounded),
                Cond(OutOfRange(150)),
                Act("jump")
            ),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Blue — WallBouncer: Uses walls as grapple anchors to slingshot across
    /// the arena at high speed. Attacks mid-flight. Creates unpredictable
    /// lateral movement that's hard to track.
    /// </summary>
    public static List<BtNode> BlueWallBouncer() =>
    [
        Sel(
            // Attached to wall with left — pull toward it, attack with right mid-flight
            Seq(
                Cond(LeftAnchored),
                Cond(LeftLocked),
                Sel(
                    Seq(
                        Cond(InAttackRange),
                        Cond(RightReady),
                        Act("launch_right_at_opponent")
                    ),
                    Act("retract_left")
                )
            ),

            // Attached to wall with right — pull and attack with left
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    Seq(
                        Cond(InAttackRange),
                        Cond(LeftReady),
                        Act("launch_left_at_opponent")
                    ),
                    Act("retract_right")
                )
            ),

            // Lock extending fists that hit a wall
            Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),

            // Near left wall — launch right to opposite wall
            Seq(Cond(AtLeftWall), Cond(RightReady), Act("launch_right_wall")),
            // Near right wall — launch left to opposite wall
            Seq(Cond(AtRightWall), Cond(LeftReady), Act("launch_left_wall")),

            // Mid-arena: launch fist toward nearest wall
            Seq(Cond(LeftReady), Act("launch_left_wall")),
            Seq(Cond(RightReady), Act("launch_right_wall")),

            // Jump to get airborne for more slingshot range
            Seq(Cond(Grounded), Act("jump")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Cyan — ZoneController: Keeps opponent at bay with staggered fist launches.
    /// One fist out as a threat/zoning tool, retracts and re-launches in rhythm.
    /// Maintains optimal distance, never lets opponent breathe.
    /// </summary>
    public static List<BtNode> CyanZoneController() =>
    [
        Sel(
            // Emergency: too close, jump and retreat
            Seq(Cond(InRange(80)), Cond(Grounded), Act("jump")),
            Seq(Cond(InRange(80)), Act("move_away_from_opponent")),

            // Stagger punches: if left is out, fire right. If right is out, fire left.
            Seq(Cond(LeftExtending), Cond(InPokeRange), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq(Cond(RightExtending), Cond(InPokeRange), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Both retracted at mid range — open with left
            Seq(Cond(LeftReady), Cond(RightReady), Cond(InPokeRange), Act("launch_left_at_opponent")),

            // Close gap if opponent is far
            Seq(Cond(OutOfRange(260)), Act("move_toward_opponent")),

            // Maintain spacing
            Seq(Cond(InRange(140)), Act("move_away_from_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Yellow — DiveKicker: Jump-heavy aerial fighter. Jumps constantly,
    /// uses downward fist launches for aerial strikes, and diagonal fists
    /// for air-to-ground pressure. Hard to pin down.
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel(
            // Airborne + descending + near opponent — dive strike
            Seq(
                Cond(Airborne),
                Cond(Descending),
                Cond(InRange(230)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Airborne + ascending — launch fists downward for coverage
            Seq(
                Cond(Airborne),
                Cond(VelY.Lt(-30)),
                Cond(InRange(200)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_down")),
                    Seq(Cond(RightReady), Act("launch_right_down"))
                )
            ),

            // Use ceiling grapple for extra air time when far
            Seq(
                Cond(Airborne),
                Cond(OutOfRange(250)),
                Cond(AtCeiling),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_up")),
                    Seq(Cond(RightReady), Act("launch_right_up"))
                )
            ),

            // Lock ceiling fist briefly for air hang
            Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
            // Quick release from ceiling
            Seq(Cond(LeftAnchored), Cond(LeftLocked), Act("retract_left")),
            Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),

            // Jump whenever grounded
            Seq(Cond(Grounded), Act("jump")),

            // Air control toward opponent
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Magenta — SwingSniper: Locks one fist to ceiling for pendulum swing,
    /// times strikes at the arc's peak velocity. Repositions by switching
    /// anchor points. Patient and precise.
    /// </summary>
    public static List<BtNode> MagentaSwingSniper() =>
    [
        Sel(
            // Right anchored, swinging — time strikes precisely
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    // At peak speed near opponent — strike
                    Seq(Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // At apex (low vel_y) and far — reanchor closer
                    Seq(Cond(VelY.Gt(-20)), Cond(VelY.Lt(20)), Cond(OutOfRange(300)), Act("retract_right")),
                    // Build swing momentum
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored, swinging — mirror
            Seq(
                Cond(LeftAnchored),
                Cond(LeftLocked),
                Sel(
                    Seq(Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq(Cond(VelY.Gt(-20)), Cond(VelY.Lt(20)), Cond(OutOfRange(300)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists that attached
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),
            Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),

            // Ground: quick punch if opponent is close before going up
            Seq(Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Launch anchor — alternate sides
            Seq(Cond(RightReady), Act("launch_right_up")),
            Seq(Cond(LeftReady), Act("launch_left_up")),

            // Jump to get off ground
            Seq(Cond(Grounded), Act("jump")),

            Act("move_toward_opponent")
        )
    ];
}
