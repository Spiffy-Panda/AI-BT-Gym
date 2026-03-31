// ─────────────────────────────────────────────────────────────────────────────
// Gen003Trees.cs — Generation 3: Evolved fighters from gen_002
// ─────────────────────────────────────────────────────────────────────────────
//
// Key insight from gen_002: Red's retreat behavior (183+ move_away per fight)
// sabotages its grappling. Cyan's flee pattern (442 move_away) is catastrophic.
// Blue's 30 wall attaches at 13 ticks avg is the gold standard.
// See generations/gen_003/CHANGES.md for details.

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;
using static AiBtGym.Godot.SubTrees;

namespace AiBtGym.Godot;

public static class Gen003Trees
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
    /// Red — CounterStriker v3: Battle data showed Red only gets 4-7 wall attaches
    /// (vs Blue's 30) and retreats 183+ times per fight. The move_away behavior at
    /// InRange(100) sabotages grappling. Fix: REMOVE the retreat-when-close behavior
    /// entirely — Red should commit to close range where counter-punching works best.
    /// Lower grapple lock threshold (110→90) for MORE anchor volume. Add attack
    /// during anchor pull for damage during approach. Keep counter-punch as priority
    /// but stop running away from fights.
    /// </summary>
    public static List<BtNode> RedCounterStriker() =>
    [
        Sel("Root priority",
            // Dodge: opponent fist incoming while close — jump (not retreat)
            DodgeFists(),

            // Anti-dive
            Seq("Anti-dive jump", Cond(Var.OpponentDirY.Gt(100)), Cond(InRange(180)), Cond(Grounded), Act("jump")),

            // Counter-punch: core identity — punish whiffs
            CounterPunch(),

            // Aerial counter-punch
            Seq("Aerial counter-punch",
                Cond(Airborne), Cond(InRange(240)),
                Sel("Detect aerial whiff", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                StrikeWithAvailable()
            ),

            // Grapple: anchored → strike with free fist, or pull toward opponent
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // NEW: Descending fast → release for dive momentum
                    Seq("Dive release left", Cond(VelY.Gt(60)), Cond(InRange(280)), Act("retract_left")),
                    Act("retract_left")
                )
            ),
            Seq("Right anchor combo",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive release right", Cond(VelY.Gt(60)), Cond(InRange(280)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Lock extending fists — EARLIER threshold (110→90) for MORE grapple volume
            AnchorLocks(200, 90),

            // Close-range poke — fire single fist aggressively
            Seq("Close-range poke", Cond(InRange(210)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Mid-range poke
            Seq("Mid-range poke",
                Cond(InRange(250)), Cond(OutOfRange(140)),
                Sel("Pick available fist",
                    Seq("Right poke", Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Left poke", Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // REMOVED: move_away_from_opponent at InRange(100) — this was causing
            // 183+ retreats per fight and sabotaging grapple approach

            // Far: launch fists for anchor opportunities
            Seq("Far left anchor launch", Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right anchor launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Stay airborne
            StayAirborne(),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Green — GrappleAssassin v3: Battle data showed Green only gets 7 wall attaches
    /// in some fights despite having grapple mechanics. Problem: lock threshold too
    /// high (130). Fix: lower to 100 for more frequent anchors. Add attack during
    /// descent for combo damage. More aggressive close-range fist firing.
    /// </summary>
    public static List<BtNode> GreenGrappleAssassin() =>
    [
        Sel("Root priority",
            // Emergency dodge
            DodgeFists(180),

            // Counter-punch
            CounterPunch(),

            // Right anchored — attack, dive-release, or pull
            Seq("Right anchor combo",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    // Strike at wider range (240)
                    Seq("Strike while anchored", Cond(InRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Dive-release
                    Seq("Dive release right", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_right")),
                    // Pull toward opponent
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike while anchored", Cond(InRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive release left", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Lock extending fists — LOWER threshold (130→100) for MORE grapple volume
            Seq("Lock right early", Cond(OutOfRange(200)), Cond(RightExtending), Cond(RightChainOver(100)), Act("lock_right")),
            Seq("Lock left early", Cond(OutOfRange(200)), Cond(LeftExtending), Cond(LeftChainOver(100)), Act("lock_left")),

            // NEW: Descending dive strike without anchor — fire during fast descent
            DiveStrike(velThreshold: 70),

            // Close-range aggression — fire single fist
            Seq("Close-range aggression", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Launch AT opponent for anchor creation
            Seq("Far right anchor launch", Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Far left anchor launch", Cond(LeftReady), Act("launch_left_at_opponent")),

            // Stay airborne
            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Blue — SwingShotgun v3: 30-5 in gen_002. Don't fix what isn't broken.
    /// Only change: slightly earlier close-range anchor lock (chain 150→140) for
    /// even tighter grapple cycling in close fights. Everything else identical.
    /// </summary>
    public static List<BtNode> BlueSwingShotgun() =>
    [
        Sel("Root priority",
            // Counter-punch
            CounterPunch(210),

            // Left anchored
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Swing strike descent", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Fast dive release", Cond(VelY.Gt(70)), Cond(InRange(350)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Right anchored
            Seq("Right anchor combo",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Swing strike descent", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Fast dive release", Cond(VelY.Gt(70)), Cond(InRange(350)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Far anchor lock
            AnchorLocks(200, 120),

            // Close-range anchor lock — TIGHTER (150→140)
            AnchorLocks(140, 140),

            // Launch
            Seq("Launch left", Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Launch right", Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Cyan — ZoneController v3: ABANDONED zoning-as-retreat. Battle data showed
    /// Cyan executes move_away_from_opponent 442 times in a 13s fight — it flees
    /// instead of zoning. 0-35 record. RADICAL REDESIGN: Cyan becomes an aggressive
    /// aerial zoner that HOLDS POSITION rather than retreating. Uses grapple anchors
    /// as fighting platforms and fires staggered pokes FROM anchor points. No more
    /// move_away_from_opponent except at extreme close range. The zoning identity is
    /// now about controlling ANGLE from anchor platforms, not distance-based retreat.
    /// </summary>
    public static List<BtNode> CyanZoneController() =>
    [
        Sel("Root priority",
            // Dodge incoming fists — jump (NOT retreat)
            DodgeFists(),

            // Counter-punch whiffs
            CounterPunch(),

            // Right anchored — FIGHT FROM ANCHOR PLATFORM (core new identity)
            Seq("Right anchor platform",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    // Fire zoning shots from anchor
                    Seq("Zone shot from anchor", Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Descending → release for dive attack
                    Seq("Dive release right", Cond(VelY.Gt(50)), Cond(InRange(280)), Act("retract_right")),
                    // Too far → pull toward opponent
                    Seq("Pull toward opponent", Cond(OutOfRange(260)), Act("retract_right")),
                    // Hold position and wait for opportunity
                    Act("retract_right")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor platform",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Zone shot from anchor", Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive release left", Cond(VelY.Gt(50)), Cond(InRange(280)), Act("retract_left")),
                    Seq("Pull toward opponent", Cond(OutOfRange(260)), Act("retract_left")),
                    Act("retract_left")
                )
            ),

            // Lock extending fists as anchor platforms — early lock for volume
            Seq("Lock right early", Cond(OutOfRange(180)), Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right")),
            Seq("Lock left early", Cond(OutOfRange(180)), Cond(LeftExtending), Cond(LeftChainOver(90)), Act("lock_left")),

            // Stagger punches at poke range (preserved identity)
            Seq("Stagger right poke", Cond(LeftExtending), Cond(InPokeRange), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Stagger left poke", Cond(RightExtending), Cond(InPokeRange), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Both ready → fire
            Seq("Close-range fire", Cond(InRange(230)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch for anchor creation
            Seq("Far left anchor launch", Cond(OutOfRange(230)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right anchor launch", Cond(OutOfRange(230)), Cond(RightReady), Act("launch_right_at_opponent")),

            // MINIMAL retreat — only at extreme close range (was at 160, now 80)
            Seq("Emergency retreat", Cond(InRange(80)), Act("move_away_from_opponent")),

            // Stay airborne
            StayAirborne(),

            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Yellow — DiveKicker v3: Lost 3-4 to Green and 1-6 to Blue in gen_002. Green
    /// beats Yellow through sustained aggression (85.9% near opponent, 101 launches).
    /// Fix: match Green's aggression with higher launch volume — remove both-fists-
    /// ready requirement for close poke. Add attack during anchor retract (damage
    /// while pulling). Earlier anchor lock maintained (90).
    /// </summary>
    public static List<BtNode> YellowDiveKicker() =>
    [
        Sel("Root priority",
            // Dodge incoming fists
            DodgeFists(180),

            // Counter-punch at wider range
            CounterPunch(),

            // Raw dive strike
            DiveStrike(),

            // Left anchored: strike, dive-release, or pull
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike while anchored", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dive release left", Cond(VelY.Gt(50)), Cond(InRange(260)), Act("retract_left")),
                    Seq("Pull when far", Cond(OutOfRange(160)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Right anchored: mirror
            Seq("Right anchor combo",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Strike while anchored", Cond(InRange(200)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dive release right", Cond(VelY.Gt(50)), Cond(InRange(260)), Act("retract_right")),
                    Seq("Pull when far", Cond(OutOfRange(160)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists — early lock (90) for grapple volume
            AnchorLocks(200, 90),

            // Close-range — REMOVED both-fists-ready requirement (was limiting offense)
            Seq("Close-range poke", Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Mid-range aggression
            Seq("Mid-range aggression",
                Cond(InRange(240)), Cond(OutOfRange(140)),
                StrikeWithAvailable()
            ),

            // Far: launch for approach anchor
            Seq("Far left anchor launch", Cond(OutOfRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right anchor launch", Cond(OutOfRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    /// <summary>
    /// Magenta — SwingSniper v3: Good recovery in gen_002 (21-12). Loses to Green
    /// 3-4 and Blue 2-5. Green beats Magenta through sustained close aggression.
    /// Fix: add aerial dodge (not just grounded) to handle airborne attackers. Tighter
    /// anchor retract range (180→170) for even faster cycling. Add close-range
    /// counter when both fists ready. Slightly wider snipe range (240→250).
    /// </summary>
    public static List<BtNode> MagentaSwingSniper() =>
    [
        Sel("Root priority",
            // Dodge incoming fists — BOTH grounded and airborne
            DodgeFists(190),

            // Counter-punch
            CounterPunch(),

            // NEW: Close-range burst — both fists ready and very close, fire immediately
            Seq("Close-range burst", Cond(InRange(150)), Cond(LeftReady), Cond(RightReady), Act("launch_left_at_opponent")),

            // Right anchored — swing-snipe or release
            Seq("Right anchor swing-snipe",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    // Swing-snipe: wider range (240→250)
                    Seq("Swing snipe shot", Cond(VelY.Gt(20)), Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // Close → attack
                    Seq("Close anchor strike", Cond(InRange(180)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    // FASTER release (180→170) for quicker cycling
                    Seq("Fast anchor release", Cond(OutOfRange(170)), Act("retract_right")),
                    Act("move_toward_opponent")
                )
            ),

            // Left anchored — mirror
            Seq("Left anchor swing-snipe",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Swing snipe shot", Cond(VelY.Gt(20)), Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Close anchor strike", Cond(InRange(180)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Fast anchor release", Cond(OutOfRange(170)), Act("retract_left")),
                    Act("move_toward_opponent")
                )
            ),

            // Lock extending fists — early lock (110)
            Seq("Lock right early", Cond(OutOfRange(190)), Cond(RightExtending), Cond(RightChainOver(110)), Act("lock_right")),
            Seq("Lock left early", Cond(OutOfRange(190)), Cond(LeftExtending), Cond(LeftChainOver(110)), Act("lock_left")),

            // Emergency close range
            Seq("Emergency close jump", Cond(InRange(120)), Cond(Grounded), Act("jump")),

            // Ground melee
            Seq("Ground melee left", Cond(Grounded), Cond(InMeleeRange), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Ground melee right", Cond(Grounded), Cond(InMeleeRange), Cond(RightReady), Act("launch_right_at_opponent")),

            // Mid-range aggressive
            Seq("Mid-range aggression",
                Cond(InRange(220)), Cond(OutOfRange(150)),
                Sel("Pick available fist",
                    Seq("Right mid strike", Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Left mid strike", Cond(LeftReady), Act("launch_left_at_opponent"))
                )
            ),

            // Launch for approach anchors
            Seq("Approach right launch", Cond(InRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),
            Seq("Approach left launch", Cond(InRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),

            // Far: launch for anchor creation
            Seq("Far anchor launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];
}
