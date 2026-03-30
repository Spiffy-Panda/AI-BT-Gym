// ─────────────────────────────────────────────────────────────────────────────
// Season3Trees.cs — Season 3 tournament: 3 veterans + 4 new challengers
// ─────────────────────────────────────────────────────────────────────────────
//
// Veterans (imported from Gen003):
//   Red_CounterStriker, Green_GrappleAssassin, Yellow_DiveKicker
//
// New challengers:
//   Orange_LowSlider     — slides underneath with one arm anchored, saves other for attack
//   White_NodeExplorer   — showcases unused node types (Parallel, Cooldown, Repeater, etc.)
//   Pink_Showboat        — flashy, wasteful, taunting — only finishes when ahead
//   Lime_MetaBreaker     — explicitly counters Season 1 Blue swing-shotgun dominance

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;
using static AiBtGym.Godot.SubTrees;

namespace AiBtGym.Godot;

public static class Season3Trees
{
    public static readonly string[] Names =
    [
        "Red_CounterStriker",
        "Green_GrappleAssassin",
        "Yellow_DiveKicker",
        "Orange_LowSlider",
        "White_NodeExplorer",
        "Pink_Showboat",
        "Lime_MetaBreaker"
    ];

    public static readonly string[] HexColors =
    [
        "#e62626", // Red
        "#26d933", // Green
        "#f2e61a", // Yellow
        "#ff8c1a", // Orange
        "#e0e0e0", // White
        "#ff69b4", // Pink
        "#80ff00"  // Lime
    ];

    public static readonly List<BtNode>[] All =
    [
        Gen003Trees.RedCounterStriker(),
        Gen003Trees.GreenGrappleAssassin(),
        Gen003Trees.YellowDiveKicker(),
        OrangeLowSlider(),
        WhiteNodeExplorer(),
        PinkShowboat(),
        LimeMetaBreaker()
    ];

    // ═════════════════════════════════════════════════════════════════════════
    // Orange — LowSlider: Anchors LEFT fist upward to create a ceiling pivot,
    // then slides underneath the opponent along the ground using move_left/
    // move_right for precise lateral control. RIGHT fist is always reserved
    // for attack — never used for grapple. The idea is to get BELOW the
    // opponent (most fighters stay airborne) and strike upward from beneath.
    // ═════════════════════════════════════════════════════════════════════════

    public static List<BtNode> OrangeLowSlider() =>
    [
        Sel("Root priority",
            // v2 fix: v1 still spammed move_left/right (156/835 ticks) because the
            // slide-under fired every tick while anchored AND close. launch_left_up
            // never creates ceiling anchors (0 ceiling, 17 wall). Only 43 dmg/fight.
            // Fix: ABANDON upward launch (doesn't work). Use LEFT fist exclusively
            // for wall-anchor approach grapple. RIGHT fist is the dedicated striker
            // (NEVER used for grapple). Slide-under only triggers on a TIGHTER
            // condition: anchored + close + opponent above us (they're airborne).
            // This creates the identity: anchor-approach → get UNDER airborne
            // opponent → right-fist uppercut from below.

            // Dodge incoming fists
            DodgeFists(180),

            // Counter-punch with RIGHT only (left is grapple-dedicated)
            Seq("Counter-punch right",
                Cond(InRange(220)),
                Sel("Opp fist retracting", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                Cond(RightReady),
                Act("launch_right_at_opponent")
            ),

            // LEFT ANCHOR: grapple approach + slide under
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchored actions",
                    // Strike from anchor at range with right
                    Seq("Anchor strike", Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // SLIDE UNDER: only when close AND opponent is ABOVE us (airborne)
                    // This is the identity — get underneath, then strike upward
                    Seq("Slide under right", Cond(InRange(200)), Cond(Var.OpponentDirY.Lt(0)), Cond(Var.OpponentDirX.Gt(0)), Act("move_right")),
                    Seq("Slide under left", Cond(InRange(200)), Cond(Var.OpponentDirY.Lt(0)), Cond(Var.OpponentDirX.Lt(0)), Act("move_left")),
                    // Dive release
                    Seq("Dive release", Cond(VelY.Gt(50)), Cond(InRange(280)), Act("retract_left")),
                    // Pull toward opponent
                    Seq("Pull toward", Cond(OutOfRange(200)), Act("retract_left")),
                    // Cycle
                    Act("retract_left")
                )
            ),

            // Lock left fist as wall anchor for approach
            Seq("Lock left anchor", Cond(OutOfRange(180)), Cond(LeftExtending), Cond(LeftChainOver(90)), Act("lock_left")),

            // Close-range right strike — always available, the bread and butter
            Seq("Close right strike", Cond(InRange(200)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Dive strike with right during fast descent
            Seq("Dive right strike",
                Cond(Airborne), Cond(VelY.Gt(60)), Cond(InRange(220)),
                Cond(RightReady), Act("launch_right_at_opponent")
            ),

            // Mid-range right poke
            Seq("Mid right poke", Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Launch left toward opponent for wall anchor creation
            Seq("Left approach launch", Cond(LeftReady), Act("launch_left_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    // ═════════════════════════════════════════════════════════════════════════
    // White — NodeExplorer: Designed to exercise EVERY unused BT node type:
    //   Parallel    — simultaneous dodge + attack checks
    //   Cooldown    — rate-limited aggression to prevent spam
    //   Repeater    — multi-pump combos
    //   Inverter    — "not in range" guards
    //   ConditionGate — gated subtrees
    // Also uses unused actions: directional launches, move_left/right, detach.
    // ═════════════════════════════════════════════════════════════════════════

    public static List<BtNode> WhiteNodeExplorer() =>
    [
        Sel("Root priority",
            // v1 fix: Diagonal launches (upleft/upright) missed opponents entirely,
            // tanking accuracy to 8.4%. Cooldown(30) was too restrictive. 79 wall
            // attaches but no damage from anchors. Detach at 100 distance too close.
            // Fix: Diagonals ONLY when far for anchoring. Lower cooldown to 15.
            // Wider detach threshold. Better close-range targeting.

            // PARALLEL: Dodge AND counter at the same time (identity)
            Par("Dodge+counter parallel", ParallelPolicy.RequireOne,
                Sel("Dodge either fist",
                    Seq("Dodge left", Cond(OppLeftExtending), Cond(InRange(200)), Cond(Grounded), Act("jump")),
                    Seq("Dodge right", Cond(OppRightExtending), Cond(InRange(200)), Cond(Grounded), Act("jump"))
                ),
                Seq("Counter whiff",
                    Sel("Either retracting", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
                    Cond(InRange(220)),
                    StrikeWithAvailable()
                )
            ),

            // Anchor combos with DETACH for quick resets (identity)
            Seq("Left anchor combo",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike from anchor", Cond(InRange(230)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // Swing strike during descent
                    Seq("Swing strike", Cond(VelY.Gt(40)), Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    // DETACH at wider range for quick reset (identity: instant release)
                    Seq("Detach close", Cond(InRange(150)), Act("detach_left")),
                    Seq("Retract far", Cond(OutOfRange(200)), Act("retract_left")),
                    Act("retract_left")
                )
            ),
            Seq("Right anchor combo",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Strike from anchor", Cond(InRange(230)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Swing strike", Cond(VelY.Gt(40)), Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Detach close", Cond(InRange(150)), Act("detach_right")),
                    Seq("Retract far", Cond(OutOfRange(200)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // CONDITIONGATE: Diagonal launches ONLY when far (for creative anchor angles)
            Gate(OutOfRange(250),
                Sel("Gated diagonal grapple",
                    Seq("Diagonal left anchor", Cond(LeftReady), Act("launch_left_upleft")),
                    Seq("Diagonal right anchor", Cond(RightReady), Act("launch_right_upright")),
                    Seq("Lock left diagonal", Cond(LeftExtending), Cond(LeftChainOver(90)), Act("lock_left")),
                    Seq("Lock right diagonal", Cond(RightExtending), Cond(RightChainOver(90)), Act("lock_right"))
                )
            ),

            // Standard anchor locks at mid-range (for normal approach grapple)
            AnchorLocks(200, 100),

            // COOLDOWN: Rate-limited aggression (every 15 ticks — was 30, too slow)
            Cool(15,
                Seq("Cooldown burst",
                    Cond(InRange(240)),
                    StrikeWithAvailable()
                )
            ),

            // INVERTER: Aerial dive strike (inverted grounded = airborne check, identity)
            Seq("Aerial dive strike",
                Inv(Cond(Grounded)),
                Cond(VelY.Gt(50)),
                Cond(InRange(230)),
                StrikeWithAvailable()
            ),

            // Close-range targeted attack (not gated by Cooldown)
            Seq("Close-range strike", Cond(InRange(180)), StrikeWithAvailable()),

            // Directional launches: fire DOWN when above opponent (identity)
            Seq("Downward strike",
                Cond(Airborne), Cond(Var.OpponentDirY.Gt(0)), Cond(InRange(180)),
                Sel("Down fist",
                    Seq("Left down", Cond(LeftReady), Act("launch_left_down")),
                    Seq("Right down", Cond(RightReady), Act("launch_right_down"))
                )
            ),

            // Lateral dodge at melee range (identity: move_left/right)
            Seq("Lateral dodge right", Cond(InRange(100)), Cond(Var.OpponentDirX.Lt(0)), Act("move_right")),
            Seq("Lateral dodge left", Cond(InRange(100)), Cond(Var.OpponentDirX.Gt(0)), Act("move_left")),

            // Far: launch toward opponent for approach
            Seq("Far left launch", Cond(OutOfRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    // ═════════════════════════════════════════════════════════════════════════
    // Pink — Showboat: Plays flashy and wasteful. Launches fists in
    // non-optimal directions, does unnecessary jumps, detaches anchors for
    // style. Only gets serious (efficient attacks) when health advantage is
    // large. The BT explicitly wastes actions when winning to "taunt".
    // ═════════════════════════════════════════════════════════════════════════

    public static List<BtNode> PinkShowboat() =>
    [
        Sel("Root priority",
            // v1 fix: Showboat threshold (>60HP, opp<80) triggered too easily — was
            // showboating when barely ahead. 11-12% accuracy due to wasted firework
            // launches. Losing 93 dmg received per fight. The detach moves threw away
            // good anchor positions.
            // Fix: Only showboat when DOMINATING (>80HP, opp<40). Fireworks only when
            // FAR and safe. Keep detach as identity but only when opp fist retracting
            // (safe window). More serious anchor combos for actual damage.

            // Emergency: if losing badly, fight seriously
            Seq("Panic mode",
                Cond(Health.Lt(40)),
                Cond(OpponentHealth.Gt(60)),
                Sel("Serious fighting",
                    CounterPunch(),
                    DiveStrike(velThreshold: 60),
                    Seq("Desperate strike", Cond(InRange(200)), StrikeWithAvailable()),
                    AnchorLocks(200, 90),
                    StayAirborne(),
                    Act("move_toward_opponent")
                )
            ),

            // Dodge — showboats still don't like getting hit
            DodgeFists(200),

            // Counter-punch (gotta look skilled)
            CounterPunch(),

            // SHOWBOATING: ONLY when dominating (>80HP, opponent <40HP)
            Seq("Showboat flourish",
                Cond(Health.Gt(80)),
                Cond(OpponentHealth.Lt(40)),
                Sel("Style moves",
                    // Fireworks ONLY when far and safe
                    Seq("Firework left", Cond(LeftReady), Cond(OutOfRange(300)), Act("launch_left_up")),
                    Seq("Firework right", Cond(RightReady), Cond(OutOfRange(300)), Act("launch_right_up")),
                    // Dramatic detach only during safe window (opponent whiffing)
                    Seq("Showoff detach left", Cond(LeftAnchored), Cond(OppLeftRetracting), Act("detach_left")),
                    Seq("Showoff detach right", Cond(RightAnchored), Cond(OppRightRetracting), Act("detach_right")),
                    // Victory hop
                    Seq("Victory hop", Cond(Grounded), Cond(OutOfRange(200)), Act("jump"))
                )
            ),

            // Anchor combos — flashy swing-strikes (identity: wide, dramatic releases)
            Seq("Left anchor show",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Flashy swing strike", Cond(VelY.Gt(40)), Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Anchor strike", Cond(InRange(220)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Dramatic wide release", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_left")),
                    Seq("Pull toward", Cond(OutOfRange(180)), Act("retract_left")),
                    Act("retract_left")
                )
            ),
            Seq("Right anchor show",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Flashy swing strike", Cond(VelY.Gt(40)), Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Anchor strike", Cond(InRange(220)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Dramatic wide release", Cond(VelY.Gt(60)), Cond(InRange(300)), Act("retract_right")),
                    Seq("Pull toward", Cond(OutOfRange(180)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Grapple locks
            AnchorLocks(200, 100),

            // Close-range aggression
            Seq("Close strike", Cond(InRange(200)), StrikeWithAvailable()),

            // Mid-range
            Seq("Mid strike", Cond(InRange(250)), StrikeWithAvailable()),

            // Far launch
            Seq("Far left launch", Cond(OutOfRange(250)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(250)), Cond(RightReady), Act("launch_right_at_opponent")),

            StayAirborne(),
            Act("move_toward_opponent")
        )
    ];

    // ═════════════════════════════════════════════════════════════════════════
    // Lime — MetaBreaker: Designed to COUNTER Season 1's dominant strategy.
    //
    // Blue_SwingShotgun (S1 #1) relies on:
    //   1. High anchor volume (30/fight) with tight cycling
    //   2. Swing-strike combos at VelY > 50 at 260 range
    //   3. Dual anchor lock phases at 200+ and 140+
    //
    // Counter strategy:
    //   - PUNISH anchor locks: when opponent fist locks, rush in and strike
    //     (opponent committed a fist, has less offense)
    //   - TIME strikes during opponent's descent (VelY > 0 = they're swinging
    //     down toward us — fire into their approach)
    //   - STAY GROUNDED more to deny aerial combo setups
    //   - HIGH close-range aggression to force trades (counter-strikers lose
    //     to sustained pressure)
    //   - FAST anchor cycling — retract at 130 distance for rapid resets
    // ═════════════════════════════════════════════════════════════════════════

    public static List<BtNode> LimeMetaBreaker() =>
    [
        Sel("Root priority",
            // v1 fix: 40-2, only lost to Yellow at avg distance 141 (too close,
            // eaten by dive-kicks). Fix: add anti-dive jump at close range when
            // opponent descending fast. Also add StayAirborne because the grounded
            // preference (only jumping when far) meant Lime stayed grounded 4-6%
            // of the time — a disadvantage vs aerial opponents.

            // Dodge fists
            DodgeFists(170),

            // ANTI-DIVE: Jump away when opponent is diving down at close range
            // This is the key fix for the Yellow matchup
            Seq("Anti-dive escape",
                Cond(InRange(180)),
                Cond(Var.OpponentDirY.Lt(0)),  // opponent above us
                Cond(Grounded),
                Act("jump")
            ),

            // ANTI-SWING: Fire into their descending approach
            Seq("Anti-swing intercept",
                Cond(Var.OpponentDirY.Lt(0)),
                Cond(InRange(280)),
                Cond(Descending),
                StrikeWithAvailable()
            ),

            // PUNISH LOCKED FISTS (identity: counter grapple-heavy meta)
            Seq("Punish opponent left lock",
                Cond(OppLeftLocked),
                Cond(InRange(250)),
                StrikeWithAvailable()
            ),
            Seq("Punish opponent right lock",
                Cond(OppRightLocked),
                Cond(InRange(250)),
                StrikeWithAvailable()
            ),

            // Counter-punch whiffs
            CounterPunch(200),

            // Close-range barrage (identity: high aggression)
            Seq("Close-range barrage",
                Cond(InRange(180)),
                StrikeWithAvailable()
            ),

            // Left anchor fast cycle
            Seq("Left anchor fast cycle",
                Cond(LeftAnchored), Cond(LeftLocked),
                Sel("Anchor left action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Swing strike descent", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(RightReady), Act("launch_right_at_opponent")),
                    Seq("Quick dive release", Cond(VelY.Gt(40)), Cond(InRange(250)), Act("retract_left")),
                    Seq("Fast retract", Cond(OutOfRange(140)), Act("retract_left")),
                    Act("retract_left")
                )
            ),
            Seq("Right anchor fast cycle",
                Cond(RightAnchored), Cond(RightLocked),
                Sel("Anchor right action",
                    Seq("Strike while anchored", Cond(InRange(210)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Swing strike descent", Cond(VelY.Gt(50)), Cond(InRange(260)), Cond(LeftReady), Act("launch_left_at_opponent")),
                    Seq("Quick dive release", Cond(VelY.Gt(40)), Cond(InRange(250)), Act("retract_right")),
                    Seq("Fast retract", Cond(OutOfRange(140)), Act("retract_right")),
                    Act("retract_right")
                )
            ),

            // Lock extending fists
            AnchorLocks(180, 100),

            // Close anchor lock
            AnchorLocks(120, 120),

            // Mid-range aggression
            Seq("Mid-range aggression",
                Cond(InRange(240)), Cond(OutOfRange(140)),
                StrikeWithAvailable()
            ),

            // Far launch
            Seq("Far left launch", Cond(OutOfRange(240)), Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Far right launch", Cond(OutOfRange(240)), Cond(RightReady), Act("launch_right_at_opponent")),

            // Stay airborne (was only jumping when far — staying grounded was a liability)
            StayAirborne(),

            Act("move_toward_opponent")
        )
    ];
}
