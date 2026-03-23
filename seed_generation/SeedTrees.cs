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
        Sel(
            // Dodge: if opponent fist is extending toward us and we're close, jump away
            Seq(Cond(OppLeftExtending), Cond(InRange(200)), Cond(Grounded), Act("jump")),
            Seq(Cond(OppRightExtending), Cond(InRange(200)), Cond(Grounded), Act("jump")),

            // Counter-punch: if opponent's fist is retracting (whiffed), punish
            Seq(
                Cond(InAttackRange),
                Sel(Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Proactive poke at mid-range with one fist, keep other ready
            Seq(Cond(InRange(250)), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Too close — back off
            Seq(Cond(InRange(100)), Act("move_away_from_opponent")),

            // Close distance if too far
            Seq(Cond(Far), Act("move_toward_opponent")),

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
        Sel(
            // Right anchored — attack with left or retract to pull closer
            Seq(
                Cond(RightAnchored), Cond(RightLocked),
                Sel(
                    // In range → strike with left
                    Seq(Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Otherwise retract to pull toward anchor (toward opponent)
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq(
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel(
                    Seq(Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Act("retract_left")
                )
            ),

            // Lock extending fists as approach anchors (only when far —
            // when close the fist will hit the opponent instead)
            Seq(Cond(OutOfRange(220)), Cond(RightExtending), Cond(RightChainOver(100)), Act("lock_right")),
            Seq(Cond(OutOfRange(220)), Cond(LeftExtending), Cond(LeftChainOver(100)), Act("lock_left")),

            // Launch AT opponent — dual purpose: hits if close, becomes
            // approach anchor if far (locked mid-flight by rules above)
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),
            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),

            // Ground fallback: jump to get airborne for grapple approach
            Seq(Cond(Grounded), Cond(OutOfRange(150)), Act("jump")),
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
        Sel(
            // Left anchored — attack with right mid-swing, then release
            Seq(
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel(
                    // In range → fire right at opponent (shotgun blast mid-swing)
                    Seq(Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Descending fast near opponent → release for dive momentum
                    Seq(Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_left")),
                    // Pull toward anchor (toward opponent)
                    Act("retract_left")
                )
            ),

            // Right anchored — mirror
            Seq(
                Cond(RightAnchored), Cond(RightLocked),
                Sel(
                    Seq(Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Lock extending fists as approach anchors (only when far)
            Seq(Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(120)), Act("lock_left")),
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(120)), Act("lock_right")),

            // Launch AT opponent — hits if close, anchors if far
            Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq(Cond(RightReady), Act("launch_right_at_opponent")),

            Seq(Cond(Grounded), Act("jump")),
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
    /// Yellow — DiveKicker: Approach-anchor grapple fighter with fist discipline
    /// and counter-punching. Uses approach anchors to close distance, strikes with
    /// the free fist, and keeps one fist in reserve for counter-punching whiffs.
    /// Re-anchors when too far from opponent to avoid stale anchors.
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel(
            // ── Counter-punch: opponent whiffed a fist, punish immediately ──
            Seq(
                Cond(InAttackRange),
                Sel(Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // ── Left anchored: pull in and strike with right ──
            Seq(
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel(
                    // Close → strike
                    Seq(Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Far → retract to pull toward opponent
                    Seq(Cond(OutOfRange(160)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // ── Right anchored: pull in and strike with left ──
            Seq(
                Cond(RightAnchored), Cond(RightLocked),
                Sel(
                    Seq(Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(OutOfRange(160)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // ── Lock extending fists as approach anchors (only when far) ──
            Seq(Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(100)), Act("lock_left")),
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(100)), Act("lock_right")),

            // ── Close-range poke: fire ONE fist, keep other for counter ──
            Seq(Cond(InRange(200)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // ── Mid-range with one fist ready: opportunistic strike ──
            Seq(Cond(InRange(250)), Cond(OutOfRange(140)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // ── Far: launch AT opponent to create approach anchor ──
            Seq(Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq(Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Jump whenever grounded
            Seq(Cond(Grounded), Act("jump")),
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
        Sel(
            // Counter-punch: if opponent whiffed, punish immediately
            Seq(
                Cond(InAttackRange),
                Sel(Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                Sel(
                    Seq(Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq(Cond(RightReady), Act("launch_right_at_opponent"))
                )
            ),

            // Right anchored — swing-snipe or pull toward opponent
            Seq(
                Cond(RightAnchored), Cond(RightLocked),
                Sel(
                    // Descending + close → snipe with left
                    Seq(Cond(VelY.Gt(15)), Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Close → attack regardless of swing phase
                    Seq(Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Not close → retract to pull toward opponent (like Blue/Green)
                    Seq(Cond(OutOfRange(200)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored — mirror
            Seq(
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel(
                    Seq(Cond(VelY.Gt(15)), Cond(InRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq(Cond(InRange(180)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq(Cond(OutOfRange(200)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists as anchors (only when far)
            Seq(Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(120)), Act("lock_right")),
            Seq(Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(120)), Act("lock_left")),

            // Emergency close range — slightly wider buffer against aggro fighters
            Seq(Cond(InRange(120)), Cond(Grounded), Act("jump")),

            // Ground melee — one fist is enough
            Seq(Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq(Cond(Grounded), Cond(InMeleeRange), Cond(RightReady), Act("launch_right_at_opponent")),

            // Mid-range poke: fire one fist, keep other for counter
            Seq(Cond(InPokeRange), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Launch AT opponent — tighter range for better accuracy
            Seq(Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq(Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch to create approach anchor
            Seq(Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            Seq(Cond(Grounded), Act("jump")),
            Act("move_toward_opponent")
        )
    ];
}
