// ─────────────────────────────────────────────────────────────────────────────
// TestTrees.cs — Collection of hardcoded BTs demonstrating movement techniques
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.When;

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
                Cond(InRange(180)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
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
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    // Attack with left fist while swinging
                    Seq(
                        Cond(InRange(200)),
                        Cond(LeftReady),
                        Act("launch_left_at_opponent")
                    ),
                    // Release if swung long enough (velocity going up = past lowest point)
                    Seq(
                        Cond(Var.VelY.Lt(-50)),
                        Act("retract_right")
                    ),
                    // Keep swinging (move in opponent's direction for momentum)
                    Act("move_toward_opponent")
                )
            ),

            // Priority 2: If right fist is attached but extending, lock it
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),

            // Priority 3: Launch right fist upward to ceiling
            Seq(Cond(RightReady), Act("launch_right_up")),

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
                Cond(LeftAnchored),
                Sel(
                    // If left is extending, lock then retract for grapple pull
                    Seq(Cond(LeftExtending), Act("lock_left")),
                    // If left is locked, retract to pull
                    Seq(Cond(LeftLocked), Act("retract_left")),
                    // Launch right to opposite direction while being pulled
                    Seq(Cond(RightReady), Act("launch_right_wall"))
                )
            ),

            // If right is attached, same but mirrored
            Seq(
                Cond(RightAnchored),
                Sel(
                    Seq(Cond(RightExtending), Act("lock_right")),
                    Seq(Cond(RightLocked), Act("retract_right")),
                    Seq(Cond(LeftReady), Act("launch_left_wall"))
                )
            ),

            // Nothing attached — launch left upward to start
            Seq(Cond(LeftReady), Act("launch_left_up")),

            // If left is out but not attached, try right
            Seq(Cond(RightReady), Act("launch_right_up")),

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
            Seq(Cond(Grounded), Cond(OutOfRange(100)), Act("jump")),
            // Punch with left
            Seq(Cond(LeftReady), Cond(InRange(250)), Act("launch_left_at_opponent")),
            // Punch with right
            Seq(Cond(RightReady), Cond(InRange(250)), Act("launch_right_at_opponent")),
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
                Cond(Var.PosY.Lt(200)),
                Cond(LeftReady),
                Cond(RightReady),
                Cond(InMeleeRange),
                Act("launch_left_at_opponent")
            ),

            // Crawl: if right is anchored to ceiling, move toward opponent, then retract right and launch left
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_up")),
                    Seq(Cond(LeftAnchored), Act("retract_right"))
                )
            ),

            // If left is anchored, similar
            Seq(
                Cond(LeftAnchored),
                Cond(LeftLocked),
                Sel(
                    Seq(Cond(RightReady), Act("launch_right_up")),
                    Seq(Cond(RightAnchored), Act("retract_left"))
                )
            ),

            // Lock extending fists that attached
            Seq(Cond(LeftAnchored), Cond(LeftExtending), Act("lock_left")),
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),

            // Start: launch right up
            Seq(Cond(RightReady), Act("launch_right_up")),
            Seq(Cond(LeftReady), Act("launch_left_up")),

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
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    // Hammer: launch left fist, lock it detached for rigid arm strike
                    Seq(Cond(LeftReady), Cond(InRange(200)), Act("launch_left_at_opponent")),
                    // Lock mid-air fist for hammer effect
                    Seq(Cond(LeftExtending), Act("lock_left")),
                    // Keep momentum going
                    Act("move_toward_opponent")
                )
            ),

            // Lock right when it attaches
            Seq(Cond(RightAnchored), Cond(RightExtending), Act("lock_right")),

            // Launch right up to ceiling
            Seq(Cond(RightReady), Act("launch_right_up")),

            Act("move_toward_opponent")
        )
    ];
}
