// ─────────────────────────────────────────────────────────────────────────────
// TestTrees.cs — Collection of hardcoded BTs demonstrating movement techniques
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;

namespace AiBtGym.Godot;

public static class TestTrees
{
    public static readonly string[] Names =
    [
        "Walker",
        "GrappleSwinger",
        "AirDancer",
        "AggressivePuncher",
        "CeilingCrawler",
        "PendulumFighter"
    ];

    public static readonly List<BtNode>[] All =
    [
        Walker(),
        GrappleSwinger(),
        AirDancer(),
        AggressivePuncher(),
        CeilingCrawler(),
        PendulumFighter()
    ];

    /// <summary>
    /// Walker: Stays on ground, moves toward opponent, punches when close.
    /// Basic ground-based fighting style.
    /// </summary>
    public static List<BtNode> Walker() =>
    [
        Sel(
            // If opponent is close and a fist is ready, punch
            Seq(
                Cond("distance_to_opponent < 180"),
                Sel(
                    Seq(Cond("left_retracted == 1"), Act("launch_left_at_opponent")),
                    Seq(Cond("right_retracted == 1"), Act("launch_right_at_opponent"))
                )
            ),
            // Otherwise walk toward opponent
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// GrappleSwinger: Launches a fist to ceiling, attaches, swings toward
    /// opponent, then launches the other fist as an attack at the arc's peak.
    /// Releases and repeats.
    /// </summary>
    public static List<BtNode> GrappleSwinger() =>
    [
        Sel(
            // Priority 1: If right fist is attached and locked, swing + attack with left
            Seq(
                Cond("right_attached == 1"),
                Cond("right_state == 2"), // Locked
                Sel(
                    // Attack with left fist while swinging
                    Seq(
                        Cond("distance_to_opponent < 200"),
                        Cond("left_retracted == 1"),
                        Act("launch_left_at_opponent")
                    ),
                    // Release if swung long enough (velocity going up = past lowest point)
                    Seq(
                        Cond("vel_y < -50"),
                        Act("retract_right")
                    ),
                    // Keep swinging (move in opponent's direction for momentum)
                    Act("move_toward_opponent")
                )
            ),

            // Priority 2: If right fist is attached but extending, lock it
            Seq(
                Cond("right_attached == 1"),
                Cond("right_state == 1"), // Extending
                Act("lock_right")
            ),

            // Priority 3: Launch right fist upward to ceiling
            Seq(
                Cond("right_retracted == 1"),
                Act("launch_right_up")
            ),

            // Fallback: move toward opponent on ground
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// AirDancer: Alternates fists between walls and ceiling to stay airborne.
    /// Launches one fist, grapples (retract while attached = pull toward wall),
    /// then launches the other to a different surface. Creates a bouncing
    /// aerial pattern.
    /// </summary>
    public static List<BtNode> AirDancer() =>
    [
        Sel(
            // If left is attached, retract it to pull ourselves there, then launch right
            Seq(
                Cond("left_attached == 1"),
                Sel(
                    // If left is extending, lock then retract for grapple pull
                    Seq(Cond("left_state == 1"), Act("lock_left")),
                    // If left is locked, retract to pull
                    Seq(Cond("left_state == 2"), Act("retract_left")),
                    // Launch right to opposite direction while being pulled
                    Seq(Cond("right_retracted == 1"), Act("launch_right_wall"))
                )
            ),

            // If right is attached, same but mirrored
            Seq(
                Cond("right_attached == 1"),
                Sel(
                    Seq(Cond("right_state == 1"), Act("lock_right")),
                    Seq(Cond("right_state == 2"), Act("retract_right")),
                    Seq(Cond("left_retracted == 1"), Act("launch_left_wall"))
                )
            ),

            // Nothing attached — launch left upward to start
            Seq(Cond("left_retracted == 1"), Act("launch_left_up")),

            // If left is out but not attached, try right
            Seq(Cond("right_retracted == 1"), Act("launch_right_up")),

            // Fallback
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// AggressivePuncher: Rushes opponent and spam-launches both fists.
    /// Pure aggression, no grappling. Jumps to close distance.
    /// </summary>
    public static List<BtNode> AggressivePuncher() =>
    [
        Sel(
            // Jump if grounded and not super close
            Seq(
                Cond("is_grounded == 1"),
                Cond("distance_to_opponent > 100"),
                Act("jump")
            ),
            // Punch with left
            Seq(
                Cond("left_retracted == 1"),
                Cond("distance_to_opponent < 250"),
                Act("launch_left_at_opponent")
            ),
            // Punch with right
            Seq(
                Cond("right_retracted == 1"),
                Cond("distance_to_opponent < 250"),
                Act("launch_right_at_opponent")
            ),
            // Close distance
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// CeilingCrawler: Attaches to the ceiling and crawls along it by
    /// alternating fists, dropping down to attack then returning up.
    /// </summary>
    public static List<BtNode> CeilingCrawler() =>
    [
        Sel(
            // If both fists are free and we're high up, drop-attack
            Seq(
                Cond("pos_y < 200"),
                Cond("left_retracted == 1"),
                Cond("right_retracted == 1"),
                Cond("distance_to_opponent < 150"),
                Act("launch_left_at_opponent")
            ),

            // Crawl: if right is anchored to ceiling, move toward opponent, then retract right and launch left
            Seq(
                Cond("right_attached == 1"),
                Cond("right_state == 2"), // Locked
                Sel(
                    Seq(Cond("left_retracted == 1"), Act("launch_left_up")),
                    Seq(Cond("left_attached == 1"), Act("retract_right"))
                )
            ),

            // If left is anchored, similar
            Seq(
                Cond("left_attached == 1"),
                Cond("left_state == 2"), // Locked
                Sel(
                    Seq(Cond("right_retracted == 1"), Act("launch_right_up")),
                    Seq(Cond("right_attached == 1"), Act("retract_left"))
                )
            ),

            // Lock extending fists that attached
            Seq(Cond("left_attached == 1"), Cond("left_state == 1"), Act("lock_left")),
            Seq(Cond("right_attached == 1"), Cond("right_state == 1"), Act("lock_right")),

            // Start: launch right up
            Seq(Cond("right_retracted == 1"), Act("launch_right_up")),
            Seq(Cond("left_retracted == 1"), Act("launch_left_up")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// PendulumFighter: Locks a fist to the ceiling and swings back and forth
    /// like a pendulum, using momentum to swing into the opponent. Uses the
    /// free fist to hammer (lock while detached = rigid arm).
    /// </summary>
    public static List<BtNode> PendulumFighter() =>
    [
        Sel(
            // If right is attached+locked (swinging), use left as a hammer
            Seq(
                Cond("right_attached == 1"),
                Cond("right_state == 2"),
                Sel(
                    // Hammer: launch left fist, lock it detached for rigid arm strike
                    Seq(
                        Cond("left_retracted == 1"),
                        Cond("distance_to_opponent < 200"),
                        Act("launch_left_at_opponent")
                    ),
                    // Lock mid-air fist for hammer effect
                    Seq(
                        Cond("left_state == 1"), // extending
                        Act("lock_left")
                    ),
                    // Keep momentum going
                    Act("move_toward_opponent")
                )
            ),

            // Lock right when it attaches
            Seq(
                Cond("right_attached == 1"),
                Cond("right_state == 1"),
                Act("lock_right")
            ),

            // Launch right up to ceiling
            Seq(
                Cond("right_retracted == 1"),
                Act("launch_right_up")
            ),

            Act("move_toward_opponent")
        )
    ];
}
