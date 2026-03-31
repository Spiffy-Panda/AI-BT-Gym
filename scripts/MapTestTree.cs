// ─────────────────────────────────────────────────────────────────────────────
// MapTestTree.cs — Adaptive BT that reacts to every arena configuration
// ─────────────────────────────────────────────────────────────────────────────

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
        Sel(
            HazardEscape(),       // P1: get off damage zones
            PickupDetour(),       // P2: grab health when hurt
            WallBusting(),        // P3: open destructible walls
            PlatformFighting(),   // P4: claim high ground
            ShrinkAwareness(),    // P5: stay in shrinking bounds
            FrictionWallPlay(),   // P6: exploit sticky walls
            DippedCeilingPlay(),  // P7: grapple via low center ceiling
            BumperEscape(),       // P8: use bumpers when cornered
            CoreCombat()          // P9: always-on fighting fallback
        )
    ];

    // ═════════════════════════════════════════════════════════════════════

    /// <summary>If standing on a hazard zone, jump off and run to center.</summary>
    private static BtNode HazardEscape() =>
        Seq("hazard_escape",
            Cond(StandingOnHazard),
            Sel(
                Seq(Cond(Grounded), Act("jump")),
                Seq(Cond("pos_x < 750"), Act("move_right")),
                Act("move_left")
            )
        );

    /// <summary>Detour to grab pickup when hurt. Wider thresholds to exercise the feature.</summary>
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

    /// <summary>Punch destructible walls open when opponent is far.</summary>
    private static BtNode WallBusting() =>
        Seq("wall_busting",
            Cond(HasWalls),
            Cond(WallStillStanding(0)),
            Cond(OutOfRange(250)),
            Sel(
                Seq(Cond(RightReady), Act("launch_right_at_opponent")),
                Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                Act("move_toward_opponent")
            )
        );

    /// <summary>
    /// Platform play: walk under the platform, jump up, land on it, hold it.
    /// Key fix: does NOT grapple (which overshoots to ceiling). Uses jump + air drift.
    /// </summary>
    private static BtNode PlatformFighting() =>
        Seq("platform_play",
            Cond(HasPlatforms),
            Sel(
                // Already on platform — hold and attack
                Seq(
                    Cond(StandingOnPlatform),
                    Sel(
                        Seq(Cond(InRange(250)),
                            Sel(
                                Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                                Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                            )
                        ),
                        Act("move_toward_opponent")
                    )
                ),

                // Grounded and near platform — jump up
                Seq(Cond(Grounded), Cond(PlatformNearby(350)), Act("jump")),

                // Airborne near platform — drift toward its center to land on it
                Seq(
                    Cond(Airborne),
                    Cond(PlatformNearby(200)),
                    Sel(
                        Seq(Cond("platform_0_x - pos_x > 20"), Act("move_right")),
                        Seq(Cond("pos_x - platform_0_x > 20"), Act("move_left"))
                    )
                ),

                // Far from platform on ground — walk toward it
                Seq(
                    Cond(Grounded),
                    Sel(
                        Seq(Cond("platform_0_x - pos_x > 30"), Act("move_right")),
                        Seq(Cond("pos_x - platform_0_x > 30"), Act("move_left"))
                    )
                )
            )
        );

    /// <summary>
    /// Shrink awareness: before shrink kicks in, play evasively to stall.
    /// Once bounds shrink, stay inside and fight aggressively.
    /// </summary>
    private static BtNode ShrinkAwareness() =>
        Seq("shrink_awareness",
            Cond(ArenaShrinking),
            Sel(
                // Bounds have actually shrunk — stay inside
                Seq(
                    Cond(ArenaLeft.Gt(50)),
                    Sel(
                        Seq(Cond("pos_x - arena_left < 80"), Act("move_right")),
                        Seq(Cond("arena_right - pos_x < 80"), Act("move_left")),
                        Seq(Cond(InRange(200)),
                            Sel(
                                Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                                Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                            )
                        ),
                        Act("move_toward_opponent")
                    )
                ),

                // Pre-shrink: play evasively — retreat when close, counter-punch
                Sel(
                    Seq(Cond(InRange(120)), Act("move_away_from_opponent")),
                    Seq(Cond(InRange(250)),
                        Sel(
                            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                            Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                        )
                    ),
                    // Stay at mid distance — don't rush in
                    Seq(Cond(OutOfRange(400)), Act("move_toward_opponent"))
                )
            )
        );

    /// <summary>
    /// Friction wall play: proactively grapple to the upper wall for aerial advantage.
    /// Phase 1: move to the wall. Phase 2: cling and attack while sliding.
    /// </summary>
    private static BtNode FrictionWallPlay() =>
        Sel("friction_wall",
            // Phase 2: already in friction zone — attack while clinging
            Seq(
                Cond(InWallFriction),
                Sel(
                    Seq(Cond(InRange(280)),
                        Sel(
                            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                            Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                        )
                    ),
                    // Re-grapple upward to stay high in the friction zone
                    Seq(Cond(RightReady), Act("launch_right_up")),
                    Seq(Cond(RightExtending), Cond(RightChainOver(60)), Act("lock_right")),
                    Seq(Cond(RightAnchored), Act("retract_right"))
                )
            ),

            // Phase 1: proactively grapple to left wall when we have HP lead.
            Seq(
                Cond(HasStickyWalls),
                Cond("health - opponent_health > 10"),
                Cond(OutOfRange(200)),  // don't abandon a close fight
                Cond(Grounded),
                // Move toward left wall
                Sel(
                    Seq(Cond("pos_x > 100"), Act("move_left")),
                    // Near left wall — jump up into friction zone
                    Act("jump")
                )
            )
        );

    /// <summary>
    /// Bumper play: use corners both defensively (escape when cornered) and
    /// offensively (retreat to corner when losing HP to slingshot back).
    /// </summary>
    private static BtNode BumperEscape() =>
        Sel("bumper_play",
            // Defensive: cornered with opponent close — jump into bumper to escape
            Seq(
                Cond(InRange(180)),
                Sel(
                    Seq(Cond("pos_x < 80"), Cond(Grounded), Act("jump")),
                    Seq(Cond("pos_x < 60"), Act("move_left")),
                    Seq(Cond("pos_x > 1420"), Cond(Grounded), Act("jump")),
                    Seq(Cond("pos_x > 1440"), Act("move_right"))
                )
            ),

            // Offensive: losing HP on bumper maps — retreat to corner for slingshot
            Seq(
                Cond(HasCornerBumpers),
                Cond("opponent_health - health > 15"),
                Cond(OutOfRange(150)),
                Sel(
                    // Closer to left corner — retreat left
                    Seq(Cond("pos_x < 750"), Act("move_left")),
                    // Closer to right corner — retreat right
                    Act("move_right")
                )
            )
        );

    /// <summary>
    /// Dipped ceiling play: on maps with a low center ceiling, use overhead grapple
    /// as a fast approach. The ceiling is lower in center so grapple anchors faster,
    /// creating a swinging aerial approach.
    /// </summary>
    private static BtNode DippedCeilingPlay() =>
        Seq("ceiling_play",
            Cond(HasDippedCeiling),
            Cond(OutOfRange(250)),
            Sel(
                // Grapple upward from center area for fast anchor on low ceiling
                Seq(Cond("pos_x > 500"), Cond("pos_x < 1000"),
                    Sel(
                        Seq(Cond(RightExtending), Cond(RightChainOver(60)), Act("lock_right")),
                        Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                        Seq(Cond(RightReady), Act("launch_right_up"))
                    )
                ),
                // Move toward center to get under the low ceiling
                Seq(Cond("pos_x < 500"), Act("move_right")),
                Seq(Cond("pos_x > 1000"), Act("move_left"))
            )
        );

    /// <summary>
    /// Core combat fallback: approach, attack in range, grapple when far.
    /// </summary>
    private static BtNode CoreCombat() =>
        Sel("core_combat",
            // Close: attack
            Seq(
                Cond(InRange(200)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Mid: grapple approach
            Seq(
                Cond(OutOfRange(200)),
                Sel(
                    Seq(Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),
                    Seq(Cond(RightAnchored), Cond(RightLocked), Act("retract_right")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // While grappling, attack with left
            Seq(Cond(RightAnchored), Cond(InRange(250)), Cond(LeftReady),
                Act("launch_left_at_opponent")),

            // Jump when far
            Seq(Cond(Grounded), Cond(OutOfRange(300)), Act("jump")),

            // Walk
            Act("move_toward_opponent")
        );
}
