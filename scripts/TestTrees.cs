// ─────────────────────────────────────────────────────────────────────────────
// TestTrees.cs — Collection of hardcoded BTs demonstrating movement techniques
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
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
    /// GrappleSwinger: Launches right fist AT the opponent, locks mid-flight
    /// for an approach anchor, swings from it and attacks with left. Retracts
    /// to re-anchor closer. Anchor always faces the opponent.
    /// </summary>
    public static List<BtNode> GrappleSwinger() =>
    [
        Sel(
            // Right anchored — swing + attack with left, or retract to pull closer
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    Seq(Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(Var.VelY.Lt(-50)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fist as anchor (when far)
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),

            // Launch right AT opponent — hits if close, anchors if far
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// AirDancer: Alternates fists launching AT the opponent to create
    /// approach anchors. Retracts to pull toward anchor, then launches
    /// the other fist for the next anchor. Creates a bouncing aerial
    /// pattern that always moves toward the opponent.
    /// </summary>
    public static List<BtNode> AirDancer() =>
    [
        Sel(
            // Left anchored — pull toward it, launch right at opponent
            Seq(
                Cond(LeftAnchored),
                Sel(
                    Seq(Cond(LeftExtending), Act("lock_left")),
                    Seq(Cond(LeftLocked), Act("retract_left")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Right anchored — mirror
            Seq(
                Cond(RightAnchored),
                Sel(
                    Seq(Cond(RightExtending), Act("lock_right")),
                    Seq(Cond(RightLocked), Act("retract_right")),
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // Nothing anchored — launch at opponent
            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),

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
    /// CeilingCrawler: Creates alternating mid-air anchors toward the opponent
    /// and traverses by retracting toward each anchor in sequence. Drops
    /// to attack when both fists are free and close, then re-anchors.
    /// </summary>
    public static List<BtNode> CeilingCrawler() =>
    [
        Sel(
            // Both fists free and close — attack
            Seq(
                Cond(LeftReady),
                Cond(RightReady),
                Cond(InRange(250)),
                Act("launch_left_at_opponent")
            ),

            // Right anchored — launch left at opponent for next anchor, then release right
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(LeftAnchored), Act("retract_right"))
                )
            ),

            // Left anchored — mirror
            Seq(
                Cond(LeftAnchored),
                Cond(LeftLocked),
                Sel(
                    Seq(Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq(Cond(RightAnchored), Act("retract_left"))
                )
            ),

            // Lock extending fists for anchor (when far)
            Seq(Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(90)), Act("lock_left")),
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),

            // Start: launch at opponent
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),
            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// PendulumFighter: Launches right fist AT the opponent, locks mid-flight
    /// to create an anchor between fighter and opponent. Swings as pendulum,
    /// uses left fist as a hammer (lock while detached = rigid arm).
    /// </summary>
    public static List<BtNode> PendulumFighter() =>
    [
        Sel(
            // Right anchored — swing and use left as hammer
            Seq(
                Cond(RightAnchored),
                Cond(RightLocked),
                Sel(
                    Seq(Cond(LeftReady), Cond(InRange(200)), Act("launch_left_at_opponent")),
                    Seq(Cond(LeftExtending), Act("lock_left")),
                    Seq(Cond(OutOfRange(400)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fist as anchor (when far)
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),

            // Launch right AT opponent — anchor if far, hit if close
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),

            Act("move_toward_opponent")
        )
    ];
}
