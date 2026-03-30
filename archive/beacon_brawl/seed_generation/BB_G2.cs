// ─────────────────────────────────────────────────────────────────────────────
// BB_G2.cs — Generation 2 Beacon Brawl teams (evolved from gen_001 analysis)
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 1 Results:
//   1. Blitz_Runners  1102 ELO (12-3) — still dominant, kill snowball works
//   2. Iron_Vanguard  1025 ELO (9-6)  — REGRESSED, hunt focus hurt beacon defense
//   3. Shadow_Flankers 949 ELO (5-10) — slight improvement, still dies too much
//   4. Bastion_Guard   924 ELO (4-11) — improved! combat-first approach works
//
// Changes for Gen 2:
//   Iron_Vanguard: Rebalance — more defend, less hunt. Fix the regression.
//   Shadow_Flankers: Reduce deaths with smarter retreat, better parry usage
//   Bastion_Guard: Double down on combat improvement, add score awareness
//   Blitz_Runners: Counter-Bastion: faster cap rotation when kills aren't available

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BB_G2
{
    public static readonly string[] Names =
    [
        "Iron_Vanguard",
        "Shadow_Flankers",
        "Bastion_Guard",
        "Blitz_Runners"
    ];

    public static readonly string[] HexColors =
    [
        "#c0392b",
        "#8e44ad",
        "#2980b9",
        "#f39c12"
    ];

    public static List<BeaconTeamEntry> GetAllEntries()
    {
        var allTrees = All;
        var entries = new List<BeaconTeamEntry>();
        for (int i = 0; i < Names.Length; i++)
            entries.Add(new BeaconTeamEntry(Names[i], allTrees[i].Trees, allTrees[i].Roles, HexColors[i]));
        return entries;
    }

    public record TeamDef(List<BtNode>[] Trees, PawnRole[] Roles);

    static BtNode C(string expr) => Cond(expr);

    public static readonly TeamDef[] All =
    [
        IronVanguard(),
        ShadowFlankers(),
        BastionGuard(),
        BlitzRunners()
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Iron_Vanguard — "Balanced Brawler"
    //    Gen 1 over-hunted, lost beacon control. Rebalancing: defend first,
    //    hunt only when safe to do so. Keep respawn exploit but after defense.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef IronVanguard() => new(
    [
        // Pawn 0: Grappler — defend-first brawler
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f), // Back to 0.2 — survive more

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn (keep from G1 but after defense)
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // DEFEND FIRST (moved up from G1)
            DefendOwnedBeacon(),

            // Engage enemies NEAR beacons only (not pure hunting)
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 350"),
                GrapplerEngage()),

            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),

            // Score-aware: only hunt when behind
            Seq("Hunt when behind", C("score_diff < -5"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            ScoreAwarePush(),
            OffensiveRotation(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_unowned")
        )],

        // Pawn 1: Gunner — beacon-anchored fire support
        [Sel(
            HealAtBase(0.25f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // Finish low HP enemies
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),

            // Fire from beacon position
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Strong defense
            DefendOwnedBeacon(),
            CapNearestUnowned(),

            Seq("Reach center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            OffensiveRotation(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Shadow_Flankers — "Survival Flanker"
    //    Gen 1 had 7.7 deaths/match (worst). Needs smarter retreat,
    //    better parry timing, and avoiding fights when low HP.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef ShadowFlankers() => new(
    [
        // Pawn 0: Grappler — smarter aggression with retreat
        [Sel(
            HookStateMachine(),
            HealAtBase(0.3f), // Back to 0.3 — stop dying so much

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // NEW: Retreat when wounded and enemy is close
            Seq("Retreat when hurt", C("my_health_pct < 0.4"), C("nearest_enemy_dist < 200"),
                Act("move_away_from_nearest_enemy")),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Only engage when healthy
            Seq("Engage when healthy", C("my_health_pct > 0.5"), C("nearest_enemy_dist < 350"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            Seq("Platform dive defend", C("in_beacon_center == 1"),
                C("enemy_count_left_beacon > 0 | enemy_count_right_beacon > 0"),
                Sel(
                    Seq(C("enemy_count_left_beacon > 0"), Act("move_toward_beacon_left")),
                    Seq(C("enemy_count_right_beacon > 0"), Act("move_toward_beacon_right"))
                )),

            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )],

        // Pawn 1: Gunner — safe kiting focus
        [Sel(
            HealAtBase(0.3f), // Higher threshold — survive

            ParryReact(),
            ExploitVulnerable(),

            // NEW: Extended kite range — stay safer
            GunnerKite(300),

            // NEW: Retreat when hurt
            Seq("Retreat when hurt", C("my_health_pct < 0.4"), C("nearest_enemy_dist < 300"),
                Act("move_away_from_nearest_enemy")),

            // Finish low HP targets
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),

            // Ricochet center
            Seq("Ricochet center", C("enemy_count_center_beacon > 0"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_wall_bounce")),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestUnowned(),

            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            Seq("Hold and shoot", C("in_beacon_left == 1 | in_beacon_right == 1"),
                C("nearest_enemy_dist < 900"), GunnerPressure()),

            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Bastion_Guard — "Combat Controller"
    //    Gen 1 improved from 2-13 to 4-11. Doubling down on combat.
    //    Adding score awareness and better kill-to-cap conversion.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BastionGuard() => new(
    [
        // Pawn 0: Grappler — aggressive controller
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn (key learning: kills enable caps)
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Engage enemies aggressively
            Seq("Hunt enemies", C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Defend beacons
            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            Seq("Platform engage", C("in_beacon_center == 1"), C("nearest_enemy_dist < 400"),
                GrapplerPlatformDive()),

            // Score-aware adaptation
            ScoreAwarePush(),
            ContestBeacon(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy") // Hunt when nothing else to do
        )],

        // Pawn 1: Gunner — aggressive fire + cap support
        [Sel(
            HealAtBase(0.2f), // Lowered — more aggressive

            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // Finish low HP enemies — key for kill snowball
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            Seq("Support center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_wall_bounce")),

            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ScoreAwarePush(),
            ContestBeacon(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Blitz_Runners — "Adaptive Blitz"
    //    Gen 1 still strong (1102 ELO). Minor tuning: faster cap rotation
    //    when kills aren't available, and better anti-defend tactics.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BlitzRunners() => new(
    [
        // Pawn 0: Grappler — hunt + fast rotate
        [Sel(
            HookStateMachine(),
            HealAtBase(0.18f), // Slightly more aggressive than G1's 0.2

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Hunt enemies
            Seq("Hunt enemies", C("nearest_enemy_dist < 450"),
                GrapplerEngage()),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // NEW: Contest enemy beacons before pushing center
            ContestBeacon(),

            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — kill-focused finisher
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(200),

            // Finish low enemies
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),
            Seq("Chase low HP", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 300"),
                Act("move_toward_nearest_enemy")),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),

            // NEW: Contest enemy beacons to freeze scoring
            ContestBeacon(),

            CapNearestUnowned(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);
}
