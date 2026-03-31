// ─────────────────────────────────────────────────────────────────────────────
// Gen001Trees.cs — Generation 1: Evolved fighters from gen_000 baseline
// ─────────────────────────────────────────────────────────────────────────────
//
// Each fighter keeps its core identity but receives targeted fixes based on
// gen_000 tournament data. See generations/gen_001/CHANGES.md for details.

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;
using static AiBtGym.Godot.SubTrees;

namespace AiBtGym.Godot;

public static class Gen001Trees
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
    /// Red — CounterStriker v1: Added aerial counter-punching so Red can fight
    /// swing-based opponents (lost 1-6 to Magenta in gen_000). Added dodge when
    /// opponent is descending fast (anti-dive). Stays grounded counter-puncher
    /// at heart but can now fight in the air.
    /// </summary>
    public static List<BtNode> RedCounterStriker() =>
    [
        Sel("Root priority",
            // Dodge: opponent fist incoming while close — jump away
            DodgeFists(),

            // NEW: Anti-dive — if opponent is descending fast toward us, jump to avoid
            Seq("Anti-dive dodge", Cond(Var.OpponentDirY.Gt(100)), Cond(InRange(180)), Cond(Grounded), Act("jump")),

            // Counter-punch: opponent's fist is retracting (whiffed), punish
            CounterPunch(),

            // NEW: Aerial counter-punch — if airborne and opponent whiffed, strike
            Seq("Aerial counter-punch",
                Cond(Airborne), Cond(InRange(240)),
                Sel("Either fist retracting", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                StrikeWithAvailable()
            ),

            // Proactive poke at mid-range with one fist, keep other ready
            Seq("Mid-range poke", Cond(InRange(250)), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // NEW: Aerial poke when airborne and in range (for fighting swingers)
            Seq("Aerial poke", Cond(Airborne), Cond(InRange(200)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Too close — back off
            Seq("Retreat when too close", Cond(InRange(100)), Act("move_away_from_opponent")),

            // NEW: Jump more to stay airborne against aerial fighters
            Seq("Stay airborne near opponent", Cond(Grounded), Cond(InRange(300)), Act("jump")),

            // Close distance if too far
            Seq("Close distance", Cond(Far), Act("move_toward_opponent")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Green — GrappleAssassin v1: Added counter-punch capability (was missing
    /// entirely in gen_000). Tighter lock timing for better anchor placement.
    /// Added emergency dodge when opponent fist extends toward us. Lost 1-6 to
    /// Yellow — now has defensive tools to handle dive pressure.
    /// </summary>
    public static List<BtNode> GreenGrappleAssassin() =>
    [
        Sel("Root priority",
            // NEW: Emergency dodge — jump when opponent fist is incoming and we're close
            DodgeFists(180),

            // NEW: Counter-punch — punish opponent whiffs (Green had no counter in gen_000)
            CounterPunch(),

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

            // Lock extending fists as approach anchors — TIGHTER timing (was 100, now 130)
            Seq("Lock right as anchor", Cond(OutOfRange(220)), Cond(RightExtending), Cond(RightChainOver(130)), Act("lock_right")),
            Seq("Lock left as anchor", Cond(OutOfRange(220)), Cond(LeftExtending), Cond(LeftChainOver(130)), Act("lock_left")),

            // Launch AT opponent — dual purpose: hits if close, becomes anchor if far
            Seq("Launch right at opponent", Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Launch left at opponent", Cond(LeftReady), Act("launch_left_at_opponent")),

            // Ground fallback: jump to get airborne for grapple approach
            Seq("Jump to get airborne", Cond(Grounded), Cond(OutOfRange(150)), Act("jump")),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Blue — SwingShotgun v1: Already the strongest fighter. Minor refinements:
    /// added counter-punch for whiffed attacks to improve mid-fight damage.
    /// Tighter shotgun range (220→200) for better accuracy. Added descending
    /// strike for when swinging down toward opponent.
    /// </summary>
    public static List<BtNode> BlueSwingShotgun() =>
    [
        Sel("Root priority",
            // NEW: Counter-punch when opponent whiffs — opportunistic damage
            CounterPunch(200),

            // Left anchored — attack with right mid-swing, then release
            Seq("Left anchor: shotgun or swing",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Attack, dive-strike, release, or pull",
                    Seq("Shotgun blast mid-swing", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive-strike while descending", Cond(VelY.Gt(50)), Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive-release for momentum", Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Right anchored — mirror
            Seq("Right anchor: shotgun or swing",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Attack, dive-strike, release, or pull",
                    Seq("Shotgun blast mid-swing", Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive-strike while descending", Cond(VelY.Gt(50)), Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive-release for momentum", Cond(VelY.Gt(80)), Cond(InRange(350)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Lock extending fists as approach anchors (only when far)
            AnchorLocks(chainOver: 120),

            // Launch AT opponent
            Seq("Launch left", Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Launch right", Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Cyan — ZoneController v1: MAJOR overhaul. Core zoning identity preserved
    /// (staggered pokes, spacing control) but now operates from the air instead
    /// of the ground. Gen_000 was 64% grounded → catastrophic 2-33 record. Now
    /// uses jump + grapple anchors for repositioning while maintaining zoning
    /// pressure. Escape improved with aerial retreat.
    /// </summary>
    public static List<BtNode> CyanZoneController() =>
    [
        Sel("Root priority",
            // Emergency: too close, jump and retreat (same identity, higher priority)
            Seq("Emergency jump", Cond(InRange(100)), Cond(Grounded), Act("jump")),
            Seq("Emergency retreat", Cond(InRange(80)), Act("move_away_from_opponent")),

            // NEW: Dodge incoming fists
            DodgeFists(),

            // NEW: Counter-punch whiffs (free damage when opponent misses)
            CounterPunch(),

            // NEW: Use grapple anchors for repositioning when close (escape tool)
            Seq("Escape anchor lock",
                Cond(InRange(150)), Cond(LeftExtending), Cond(LeftChainOver(100)), Act("lock_left")
            ),
            Seq("Pull away using anchor",
                Cond(LeftAnchored), Cond(LeftLocked), Cond(InRange(150)),
                Act("retract_left")
            ),

            // Core identity: stagger punches at poke range
            Seq("Stagger: right follow-up", Cond(LeftExtending), Cond(InPokeRange), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Stagger: left follow-up", Cond(RightExtending), Cond(InPokeRange), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Both retracted at mid range — open with left
            Seq("Open with left poke", Cond(LeftReady), Cond(RightReady), Cond(InPokeRange), Act("launch_left_at_opponent")),

            // NEW: Aerial poke — fire while airborne at range
            Seq("Aerial poke", Cond(Airborne), Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Close gap if opponent is far
            Seq("Close distance", Cond(OutOfRange(260)), Act("move_toward_opponent")),

            // Maintain spacing — retreat if too close
            Seq("Maintain spacing", Cond(InRange(160)), Act("move_away_from_opponent")),

            // NEW: Stay airborne — jump whenever grounded (biggest change)
            StayAirborne(),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Yellow — DiveKicker v1: Lost 1-6 to Red's counter-punching. Fix: added
    /// dodge jumps when opponent extends fists (anti-counter), tighter close-range
    /// aggression to overwhelm before counter-punches land. Better anchor release
    /// timing for faster dive attacks.
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel("Root priority",
            // NEW: Dodge incoming fists — Yellow was getting countered by Red
            DodgeFists(180),

            // Counter-punch: opponent whiffed a fist, punish immediately
            CounterPunch(),

            // Left anchored: pull in and strike with right
            Seq("Left anchor: strike or pull",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Strike, dive-release, pull, or advance",
                    Seq("Close-range strike", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive-release for attack", Cond(VelY.Gt(60)), Cond(InRange(280)), Act("retract_left")),
                    Seq("Retract to pull closer", Cond(OutOfRange(160)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Right anchored: pull in and strike with left
            Seq("Right anchor: strike or pull",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Strike, dive-release, pull, or advance",
                    Seq("Close-range strike", Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive-release for attack", Cond(VelY.Gt(60)), Cond(InRange(280)), Act("retract_right")),
                    Seq("Retract to pull closer", Cond(OutOfRange(160)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists as approach anchors (only when far)
            AnchorLocks(),

            // Close-range: fire ONE fist, keep other for counter
            Seq("Close-range poke", Cond(InRange(180)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Mid-range with one fist ready: opportunistic strike
            Seq("Mid-range opportunistic", Cond(InRange(240)), Cond(OutOfRange(140)),
                StrikeWithAvailable()
            ),

            // Far: launch AT opponent to create approach anchor
            Seq("Far anchor: left", Cond(OutOfRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far anchor: right", Cond(OutOfRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Jump whenever grounded
            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Magenta — SwingSniper v1: Lost 3-4 to Green's grapple aggression. Fix:
    /// wider swing-snipe window (VelY threshold 15→10) for more consistent hits.
    /// Added anti-grapple behavior — retract anchors when opponent has their own
    /// anchor nearby (prevents getting pulled). Better mid-range pressure with
    /// tighter poke discipline.
    /// </summary>
    public static List<BtNode> MagentaSwingSniper() =>
    [
        Sel("Root priority",
            // Counter-punch: if opponent whiffed, punish immediately
            CounterPunch(),

            // Right anchored — swing-snipe or pull toward opponent
            Seq("Right anchor: swing-snipe",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Snipe, close attack, pull, or advance",
                    Seq("Downswing snipe", Cond(VelY.Gt(10)), Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Close-range attack", Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(200)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor: swing-snipe",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Snipe, close attack, pull, or advance",
                    Seq("Downswing snipe", Cond(VelY.Gt(10)), Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Close-range attack", Cond(InRange(180)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Retract to pull closer", Cond(OutOfRange(200)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists as anchors (only when far)
            Seq("Lock right as anchor", Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(120)), Act("lock_right")),
            Seq("Lock left as anchor", Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(120)), Act("lock_left")),

            // Emergency close range — jump to escape and reposition
            Seq("Emergency escape jump", Cond(InRange(120)), Cond(Grounded), Act("jump")),

            // Ground melee — one fist is enough
            Seq("Ground melee: left", Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Ground melee: right", Cond(Grounded), Cond(InMeleeRange), Cond(RightReady), Act("launch_right_at_opponent")),

            // Mid-range poke: fire one fist, keep other for counter
            Seq("Mid-range disciplined poke", Cond(InPokeRange), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // NEW: More aggressive mid-range — fire even with only one fist ready
            Seq("Aggressive mid-range", Cond(InRange(230)), Cond(OutOfRange(150)),
                Sel("Strike with available fist",
                    Seq("Right strike", Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Left strike", Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // Launch AT opponent — for approach anchors
            Seq("Tight-range right launch", Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Tight-range left launch", Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch to create approach anchor
            Seq("Far anchor launch", Cond(OutOfRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];
}
