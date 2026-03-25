// ─────────────────────────────────────────────────────────────────────────────
// SeedTrees.cs — 6 competitive seed AIs for evolutionary tournament seeding
// Colors: Red, Green, Blue, Cyan, Yellow, Magenta
// ─────────────────────────────────────────────────────────────────────────────
//
// Fists anchor mid-air when locked — no surface needed.
// Lock during Extending creates a pivot point at the fist's position.

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;
using static AiBtGym.Godot.SubTrees;

namespace AiBtGym.Godot;

public static class SeedTrees
{
    public static readonly string[] Names =
    [
        "Red_CounterStriker",
        "Green_GrappleAssassin",
        "Blue_SwingShotgun",
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

    public static readonly string[] HexColors =
    [
        "#e62626", // Red
        "#26d933", // Green
        "#3359f2", // Blue
        "#1ae5e5", // Cyan
        "#f2e61a", // Yellow
        "#e626d9"  // Magenta
    ];

    public static readonly List<BtNode>[] All =
    [
        RedCounterStriker(),
        GreenGrappleAssassin(),
        BlueSwingShotgun(),
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
        Sel("Root priority",
            DodgeFists(),
            CounterPunch(),

            // Proactive poke at mid-range with one fist, keep other ready
            Seq("Mid-range poke", Cond(InRange(250)), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Too close — back off
            Seq("Retreat when too close", Cond(InRange(100)), Act("move_away_from_opponent")),

            // Close distance if too far
            Seq("Close distance", Cond(Far), Act("move_toward_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Green — GrappleAssassin: Launches fists AT the opponent, locks mid-flight
    /// to create approach anchors between fighter and opponent, then retracts
    /// to pull toward them. At close range, fists hit instead of anchoring.
    /// Free-space anchors always point toward the opponent, preventing
    /// ceiling-float.
    /// </summary>
    public static List<BtNode> GreenGrappleAssassin() =>
    [
        Sel("Root priority",
            // Right anchored — attack with left or retract to pull closer
            Seq("Right anchor: strike or pull",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Strike or retract",
                    Seq("Strike with left", Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor: strike or pull",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Strike or retract",
                    Seq("Strike with right", Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Act("retract_left")
                )
            ),

            AnchorLocks(outOfRange: 220, chainOver: 100),

            // Launch AT opponent — dual purpose: hits if close, becomes
            // approach anchor if far (locked mid-flight by rules above)
            Seq("Launch right at opponent", Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Launch left at opponent", Cond(LeftReady), Act("launch_left_at_opponent")),

            // Ground fallback: jump to get airborne for grapple approach
            Seq("Jump to get airborne", Cond(Grounded), Cond(OutOfRange(150)), Act("jump")),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Blue — SwingShotgun: Rapid grapple traversal toward opponent. Launches
    /// fists AT the opponent, locks mid-flight for approach anchors, swings
    /// through and fires the other fist as an attack. Alternates fists for
    /// continuous forward pressure. Anchors always face the opponent.
    /// </summary>
    public static List<BtNode> BlueSwingShotgun() =>
    [
        Sel("Root priority",
            // Left anchored — attack with right mid-swing, then release
            Seq("Left anchor: shotgun or swing",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Attack, dive-release, or pull",
                    Seq("Shotgun blast mid-swing", Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive-release for momentum", Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Right anchored — mirror
            Seq("Right anchor: shotgun or swing",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Attack, dive-release, or pull",
                    Seq("Shotgun blast mid-swing", Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive-release for momentum", Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            AnchorLocks(outOfRange: 200, chainOver: 120),

            // Launch AT opponent — hits if close, anchors if far
            Seq("Launch left", Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Launch right", Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Cyan — ZoneController: Keeps opponent at bay with staggered fist launches.
    /// One fist out as a threat/zoning tool, the other ready to fire.
    /// Maintains optimal distance, never lets opponent breathe.
    /// </summary>
    public static List<BtNode> CyanZoneController() =>
    [
        Sel("Root priority",
            // Emergency: too close, jump and retreat
            Seq("Emergency jump", Cond(InRange(80)), Cond(Grounded), Act("jump")),
            Seq("Emergency retreat", Cond(InRange(80)), Act("move_away_from_opponent")),

            // Stagger punches: if left is out, fire right. If right is out, fire left.
            Seq("Stagger: right follow-up", Cond(LeftExtending), Cond(InPokeRange), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Stagger: left follow-up", Cond(RightExtending), Cond(InPokeRange), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Both retracted at mid range — open with left
            Seq("Open with left poke", Cond(LeftReady), Cond(RightReady), Cond(InPokeRange), Act("launch_left_at_opponent")),

            // Close gap if opponent is far
            Seq("Close distance", Cond(OutOfRange(260)), Act("move_toward_opponent")),

            // Maintain spacing
            Seq("Maintain spacing", Cond(InRange(140)), Act("move_away_from_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Yellow — DiveKicker: Approach-anchor grapple fighter with fist discipline
    /// and counter-punching. Uses approach anchors to close distance, strikes with
    /// the free fist, and keeps one fist in reserve for counter-punching whiffs.
    /// Re-anchors when too far from opponent to avoid stale anchors.
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel("Root priority",
            CounterPunch(),

            // ── Left anchored: pull in and strike with right ──
            Seq("Left anchor: strike or pull",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Strike, pull, or advance",
                    Seq("Close-range strike", Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(160)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // ── Right anchored: pull in and strike with left ──
            Seq("Right anchor: strike or pull",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Strike, pull, or advance",
                    Seq("Close-range strike", Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(160)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            AnchorLocks(),

            // ── Close-range poke: fire ONE fist, keep other for counter ──
            Seq("Close-range poke", Cond(InRange(200)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // ── Mid-range with one fist ready: opportunistic strike ──
            Seq("Mid-range opportunistic", Cond(InRange(250)), Cond(OutOfRange(140)),
                StrikeWithAvailable()
            ),

            // ── Far: launch AT opponent to create approach anchor ──
            Seq("Far anchor: left", Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far anchor: right", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Magenta — SwingSniper: Launches a fist AT the opponent, locks mid-flight
    /// to create an anchor between fighter and opponent. Swings as pendulum
    /// from this anchor, timing strikes during the downswing for maximum
    /// momentum. Counter-punches whiffs, retreats when vulnerable.
    /// </summary>
    public static List<BtNode> MagentaSwingSniper() =>
    [
        Sel("Root priority",
            CounterPunch(),

            // Right anchored — swing-snipe or pull toward opponent
            Seq("Right anchor: swing-snipe",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Snipe, close attack, pull, or advance",
                    Seq("Downswing snipe", Cond(VelY.Gt(15)), Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Close-range attack", Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(200)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor: swing-snipe",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Snipe, close attack, pull, or advance",
                    Seq("Downswing snipe", Cond(VelY.Gt(15)), Cond(InRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Close-range attack", Cond(InRange(180)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(200)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            AnchorLocks(outOfRange: 200, chainOver: 120),

            // Emergency close range — slightly wider buffer against aggro fighters
            Seq("Emergency escape jump", Cond(InRange(120)), Cond(Grounded), Act("jump")),

            // Ground melee — one fist is enough
            Seq("Ground melee: left", Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Ground melee: right", Cond(Grounded), Cond(InMeleeRange), Cond(RightReady), Act("launch_right_at_opponent")),

            // Mid-range poke: fire one fist, keep other for counter
            Seq("Mid-range disciplined poke", Cond(InPokeRange), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Launch AT opponent — tighter range for better accuracy
            Seq("Tight-range right launch", Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Tight-range left launch", Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch to create approach anchor
            Seq("Far anchor launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];
}
