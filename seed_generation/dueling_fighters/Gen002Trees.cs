// ─────────────────────────────────────────────────────────────────────────────
// Gen002Trees.cs — Generation 2: Evolved fighters from gen_001
// ─────────────────────────────────────────────────────────────────────────────
//
// Key insight from gen_001: grapple volume is the meta. Blue dominates with 56
// wall attaches per fight. Red has ZERO grappling and is falling behind. Cyan
// still fundamentally broken despite going aerial. See gen_002/CHANGES.md.

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;
using static AiBtGym.Godot.SubTrees;

namespace AiBtGym.Godot;

public static class Gen002Trees
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
    /// Red — CounterStriker v2: BIGGEST CHANGE — Red now uses grapple anchors for
    /// repositioning. In gen_001 Red had 0 wall attaches in every battle and dropped
    /// to 952 ELO. Now launches fists that become approach anchors when far, retracts
    /// to close distance, then counter-punches at close range. Core counter-punch
    /// identity preserved but with grapple mobility. Also: require only one fist
    /// ready for proactive pokes (was requiring both, limiting offense).
    /// </summary>
    public static List<BtNode> RedCounterStriker() =>
    [
        Sel("Root priority",
            // Dodge: opponent fist incoming while close — jump away
            DodgeFists(),

            // Anti-dive — opponent descending fast toward us
            Seq("Anti-dive escape", Cond(Var.OpponentDirY.Gt(100)), Cond(InRange(180)), Cond(Grounded), Act("jump")),

            // Counter-punch: opponent whiffed, punish (core identity)
            CounterPunch(),

            // Aerial counter-punch
            Seq("Aerial counter-punch",
                Cond(Airborne), Cond(InRange(240)),
                Sel("Opponent retracting", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                StrikeWithAvailable()
            ),

            // NEW: Grapple anchor — when anchored, pull toward opponent then strike
            Seq("Left anchor approach",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Strike or pull",
                    // Close enough → strike with right
                    Seq("Close right strike", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Pull toward opponent
                    Act("retract_left")
                )
            ),
            Seq("Right anchor approach",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Strike or pull",
                    Seq("Close left strike", Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Act("retract_right")
                )
            ),

            // NEW: Lock extending fists as approach anchors when far
            AnchorLocks(outOfRange: 220, chainOver: 110),

            // Proactive poke — RELAXED: only need one fist ready (was requiring both)
            Seq("Proactive mid poke", Cond(InRange(230)), Cond(OutOfRange(140)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Aerial poke
            Seq("Aerial poke", Cond(Airborne), Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Too close — back off
            Seq("Retreat if too close", Cond(InRange(100)), Act("move_away_from_opponent")),

            // Far: launch fist toward opponent (becomes anchor if locked above)
            Seq("Far left launch", Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Stay airborne
            Seq("Jump if grounded", Cond(Grounded), Act("jump")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Green — GrappleAssassin v2: Improved anti-Blue tactics. In gen_001, Green
    /// lost 2-5 to Blue despite having good grapple mechanics. Fix: when anchored
    /// and descending fast, release anchor early for dive momentum (like Blue does).
    /// Also: attack while anchored at wider range (220→240) and fire single fists
    /// more aggressively at mid-range instead of waiting for both ready.
    /// </summary>
    public static List<BtNode> GreenGrappleAssassin() =>
    [
        Sel("Root priority",
            // Emergency dodge
            DodgeFists(180),

            // Counter-punch
            CounterPunch(),

            // Right anchored — attack with left or retract/release
            Seq("Right anchor tactics",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Attack or reposition",
                    // WIDER attack range: 220→240
                    Seq("Wide left strike", Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // NEW: Descending fast → release for dive momentum (learned from Blue)
                    Seq("Dive release", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_right")),
                    // Pull toward anchor
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor tactics",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Attack or reposition",
                    Seq("Wide right strike", Cond(InRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive release", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Lock extending fists as approach anchors
            AnchorLocks(outOfRange: 220, chainOver: 130),

            // NEW: Mid-range aggression — fire single fist without requiring both ready
            Seq("Mid-range aggression", Cond(InRange(200)), Cond(OutOfRange(120)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Launch AT opponent
            Seq("Launch right", Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Launch left", Cond(LeftReady), Act("launch_left_at_opponent")),

            // Jump to get airborne
            Seq("Jump if grounded", Cond(Grounded), Act("jump")),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Blue — SwingShotgun v2: Already dominant at 1183 ELO. Minimal tuning only.
    /// Fix: slightly wider attack windows while anchored to convert more swings
    /// into hits (200→210). Added fallback anchor lock at closer range for when
    /// fights get tight. Reduced dive-release threshold for faster transitions.
    /// </summary>
    public static List<BtNode> BlueSwingShotgun() =>
    [
        Sel("Root priority",
            // Counter-punch when opponent whiffs
            CounterPunch(210),

            // Left anchored — attack with right mid-swing
            Seq("Left anchor swing",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Shotgun or reposition",
                    // WIDER shotgun range: 200→210
                    Seq("Wide right strike", Cond(InRange(210)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Descending fast → strike while diving
                    Seq("Dive right strike", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Descending fast near opponent → release for momentum (LOWER threshold: 80→70)
                    Seq("Dive release", Cond(VelY.Gt(70)), Cond(InRange(350)), Act("retract_left")),
                    // Pull toward anchor
                    Act("retract_left")
                )
            ),

            // Right anchored — mirror
            Seq("Right anchor swing",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Shotgun or reposition",
                    Seq("Wide left strike", Cond(InRange(210)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive left strike", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive release", Cond(VelY.Gt(70)), Cond(InRange(350)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Lock extending fists as approach anchors (far)
            AnchorLocks(outOfRange: 200, chainOver: 120),

            // NEW: Close-range anchor lock for tighter grapple fights
            AnchorLocks(outOfRange: 140, chainOver: 150),

            // Launch AT opponent
            Seq("Launch left", Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Launch right", Cond(RightReady), Act("launch_right_at_opponent")),

            Seq("Jump if grounded", Cond(Grounded), Act("jump")),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Cyan — ZoneController v2: REDESIGNED as aerial grapple-zoner. Gen_001 changes
    /// (going aerial, dodge jumps) barely helped (3-32). Root cause: Cyan's "retreat
    /// and poke" strategy wastes too many launches at bad angles. Gen_002: adopt
    /// grapple-based approach like the winning fighters but maintain zoning spacing.
    /// Use anchors to MAINTAIN distance (not close it), fire zoning shots from
    /// anchor points. Both fists now contribute to grapple+zone pattern.
    /// </summary>
    public static List<BtNode> CyanZoneController() =>
    [
        Sel("Root priority",
            // Emergency close → jump away
            Seq("Emergency jump away", Cond(InRange(100)), Cond(Grounded), Act("jump")),
            Seq("Emergency retreat", Cond(InRange(80)), Act("move_away_from_opponent")),

            // Dodge incoming fists
            DodgeFists(),

            // Counter-punch whiffs
            CounterPunch(),

            // NEW: Right anchored — use as zoning platform. Strike with left, retract to reposition
            Seq("Right anchor zoning",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Zone or reposition",
                    // Zone shot from anchor point
                    Seq("Zone left strike", Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Too far from opponent → retract to get closer
                    Seq("Retract if too far", Cond(OutOfRange(280)), Act("retract_right")),
                    // Too close → move away (zone identity)
                    Seq("Retreat if too close", Cond(InRange(140)), Act("move_away_from_opponent")),
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor zoning",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Zone or reposition",
                    Seq("Zone right strike", Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Retract if too far", Cond(OutOfRange(280)), Act("retract_left")),
                    Seq("Retreat if too close", Cond(InRange(140)), Act("move_away_from_opponent")),
                    Act("retract_left")
                )
            ),

            // NEW: Lock extending fists as grapple anchors (both range tiers)
            AnchorLocks(outOfRange: 200, chainOver: 110),

            // Stagger punches at poke range (core identity preserved)
            Seq("Stagger right poke", Cond(LeftExtending), Cond(InPokeRange), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Stagger left poke", Cond(RightExtending), Cond(InPokeRange), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Both ready at poke range → open with left
            Seq("Open with left poke", Cond(LeftReady), Cond(RightReady), Cond(InPokeRange), Act("launch_left_at_opponent")),

            // NEW: Far launch — creates anchor opportunities
            Seq("Far left launch", Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Maintain spacing — retreat if too close
            Seq("Maintain zone spacing", Cond(InRange(160)), Act("move_away_from_opponent")),

            // Close gap if very far
            Seq("Close gap if far", Cond(OutOfRange(300)), Act("move_toward_opponent")),

            // Stay airborne
            Seq("Jump if grounded", Cond(Grounded), Act("jump")),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Yellow — DiveKicker v2: Lost 3-4 to Green in gen_001 and 1-5 to Blue.
    /// Fix: adopt Blue's faster anchor cycling — lock earlier for more attachment
    /// volume (100→90 chain threshold). Release anchors sooner on descent for
    /// more dynamic dive attacks. Add mid-flight strike when descending fast
    /// without anchor (raw dive attack). Tighter counter-punch to punish at
    /// wider range.
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel("Root priority",
            // Dodge incoming fists
            DodgeFists(180),

            // Counter-punch — WIDER range (InAttackRange → InRange(220))
            CounterPunch(),

            // NEW: Raw dive strike — descending fast near opponent without anchor
            DiveStrike(),

            // Left anchored: strike or release
            Seq("Left anchor dive",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Strike or release",
                    // Close → strike
                    Seq("Close right strike", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Descending → release for dive (LOWER threshold: 60→50)
                    Seq("Dive release", Cond(VelY.Gt(50)), Cond(InRange(260)), Act("retract_left")),
                    // Far → retract to pull
                    Seq("Pull toward anchor", Cond(OutOfRange(160)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Right anchored: mirror
            Seq("Right anchor dive",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Strike or release",
                    Seq("Close left strike", Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive release", Cond(VelY.Gt(50)), Cond(InRange(260)), Act("retract_right")),
                    Seq("Pull toward anchor", Cond(OutOfRange(160)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists — EARLIER lock (100→90) for more grapple volume
            AnchorLocks(outOfRange: 200, chainOver: 90),

            // Close-range single fist
            Seq("Close double-ready poke", Cond(InRange(180)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Mid-range single fist
            Seq("Mid-range poke",
                Cond(InRange(240)), Cond(OutOfRange(140)),
                StrikeWithAvailable()
            ),

            // Far: launch for approach anchor
            Seq("Far left launch", Cond(OutOfRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),

            Seq("Jump if grounded", Cond(Grounded), Act("jump")),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Magenta — SwingSniper v2: Collapsed to 0-7 vs Blue in gen_001. Battle data
    /// shows Blue outgrapples Magenta with 56 attaches vs Magenta's ~24. Key issue:
    /// Magenta's long anchor durations (20.4 ticks avg) make it predictable and
    /// slow to cycle. Fix: faster anchor cycling — retract at closer range (200→180)
    /// so anchors are shorter. Add dodge/escape when opponent is close and extending.
    /// Swing-snipe window tightened back (VelY 10→20) to avoid premature attacks.
    /// </summary>
    public static List<BtNode> MagentaSwingSniper() =>
    [
        Sel("Root priority",
            // NEW: Dodge incoming fists (was missing — got hit by Blue's counter-punches)
            DodgeFists(190),

            // Counter-punch
            CounterPunch(),

            // Right anchored — swing-snipe or release
            Seq("Right anchor snipe",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Snipe or reposition",
                    // Swing-snipe: tightened back (VelY 10→20 for more accurate timing)
                    Seq("Swing snipe left", Cond(VelY.Gt(20)), Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Close → attack
                    Seq("Close left strike", Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // FASTER release — retract at 180 instead of 200 (shorter anchor durations)
                    Seq("Fast retract", Cond(OutOfRange(180)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor snipe",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Snipe or reposition",
                    Seq("Swing snipe right", Cond(VelY.Gt(20)), Cond(InRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Close right strike", Cond(InRange(180)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Fast retract", Cond(OutOfRange(180)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists — slightly EARLIER (120→110) for more grapple volume
            AnchorLocks(outOfRange: 190, chainOver: 110),

            // Emergency close range
            Seq("Emergency close jump", Cond(InRange(120)), Cond(Grounded), Act("jump")),

            // Ground melee
            Seq("Ground melee left", Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Ground melee right", Cond(Grounded), Cond(InMeleeRange), Cond(RightReady), Act("launch_right_at_opponent")),

            // Mid-range poke with discipline
            Seq("Disciplined mid poke", Cond(InPokeRange), Cond(OutOfRange(140)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Aggressive mid-range single fist
            Seq("Aggressive mid poke",
                Cond(InRange(220)), Cond(OutOfRange(150)),
                Sel("Strike with available fist",
                    Seq("Right strike", Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Left strike", Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // Launch for approach anchors
            Seq("Mid right launch", Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Mid left launch", Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch to create approach anchor
            Seq("Far right launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            Seq("Jump if grounded", Cond(Grounded), Act("jump")),
            Act("move_toward_opponent")
        )
    ];
}
