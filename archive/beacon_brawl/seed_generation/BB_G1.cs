// ─────────────────────────────────────────────────────────────────────────────
// BB_G1.cs — Generation 1 Beacon Brawl teams (evolved from gen_000 analysis)
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 0 Results:
//   1. Blitz_Runners  1099 ELO (12-3) — kill snowball dominant
//   2. Iron_Vanguard  1088 ELO (12-3) — parry brawler strong but loses to Blitz
//   3. Shadow_Flankers 923 ELO (4-11) — split-cap too passive
//   4. Bastion_Guard   891 ELO (2-13) — center fortress one-dimensional
//
// Changes for Gen 1:
//   Iron_Vanguard: Add respawn exploitation, lower heal threshold to stay aggressive
//   Shadow_Flankers: Add kill pressure, better combat before capping
//   Bastion_Guard: Abandon pure center strategy, spread map control + add combat
//   Blitz_Runners: Better beacon defense, reduce unnecessary deaths

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BB_G1
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
    // 1. Iron_Vanguard — "Parry Brawler + Respawn Exploit"
    //    Gen 0 lost to Blitz 2-3. Adding respawn exploitation and
    //    lowering heal threshold to match Blitz's aggression.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef IronVanguard() => new(
    [
        // Pawn 0: Grappler — parry brawler with respawn exploit
        [Sel(
            HookStateMachine(),
            HealAtBase(0.15f), // Lowered from 0.2 — stay aggressive like Blitz

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // NEW: Cap during enemy respawn windows (learned from Blitz)
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Engage enemies near our beacons
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // NEW: Hunt when ahead — press the advantage
            Seq("Hunt when ahead", C("score_diff > 15"), C("nearest_enemy_dist < 500"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ScoreAwarePush(),
            OffensiveRotation(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy") // Changed: hunt instead of passive move
        )],

        // Pawn 1: Gunner — aggressive fire support with respawn exploit
        [Sel(
            HealAtBase(0.2f), // Lowered from 0.25

            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // NEW: Finish low HP enemies for kill bonus
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),

            // Sustained pressure from beacon
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // NEW: Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

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
    // 2. Shadow_Flankers — "Aggressive Flanker"
    //    Gen 0 was 0-5 vs Iron, 1-4 vs Blitz. Too passive.
    //    Adding kill pressure before capping, lower heal threshold.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef ShadowFlankers() => new(
    [
        // Pawn 0: Grappler — combat-first flanker
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f), // Lowered from 0.3

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(), // NEW: added lockout punishment

            // NEW: Prioritize kills to create respawn windows
            Seq("Hunt enemies", C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            // Dive from platform to contest
            Seq("Platform dive defend", C("in_beacon_center == 1"),
                C("enemy_count_left_beacon > 0 | enemy_count_right_beacon > 0"),
                Sel(
                    Seq(C("enemy_count_left_beacon > 0"), Act("move_toward_beacon_left")),
                    Seq(C("enemy_count_right_beacon > 0"), Act("move_toward_beacon_right"))
                )),

            ContestBeacon(),
            ScoreAwarePush(), // NEW: score-aware adaptation
            LateGamePush(),
            Act("move_toward_nearest_enemy") // Changed: hunt instead of move to unowned
        )],

        // Pawn 1: Gunner — aggressive zone controller
        [Sel(
            HealAtBase(0.2f), // Lowered from 0.25
            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(), // NEW

            GunnerKite(250),

            // NEW: Finish low HP targets
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
    // 3. Bastion_Guard — "Adaptive Defender"
    //    Gen 0 was 2-13, only 34% beacon ownership. Center fortress failed.
    //    Spreading map control, adding combat, and being less center-obsessed.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BastionGuard() => new(
    [
        // Pawn 0: Grappler — flexible defender (not center-locked)
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // NEW: Kill pressure first — create respawn windows
            Seq("Hunt close enemies", C("nearest_enemy_dist < 350"),
                GrapplerEngage()),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Defend owned beacons (center still prioritized but not exclusive)
            DefendOwnedBeacon(),

            // Cap sides FIRST (changed from center-first)
            CapNearestSideFirst(),

            // Only push center after having sides
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            // Platform dive when holding center
            Seq("Platform engage", C("in_beacon_center == 1"), C("nearest_enemy_dist < 400"),
                GrapplerPlatformDive()),

            ScoreAwarePush(),
            ContestBeacon(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_unowned")
        )],

        // Pawn 1: Gunner — mobile fire support (not just side capper)
        [Sel(
            HealAtBase(0.25f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // NEW: Finish low HP enemies
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Ricochet support for center
            Seq("Support center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_wall_bounce")),

            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ContestBeacon(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Blitz_Runners — "Refined Kill Snowball"
    //    Gen 0 was top at 1099 ELO. Refining to reduce deaths (7.27 avg)
    //    and improve beacon defense after kills.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BlitzRunners() => new(
    [
        // Pawn 0: Grappler — smarter hunter (retreat when low)
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f), // Raised from 0.15 — survive more, die less

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Hunt enemies aggressively
            Seq("Hunt enemies", C("nearest_enemy_dist < 500"),
                GrapplerEngage()),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // NEW: Defend beacons higher priority (was losing beacons while hunting)
            DefendOwnedBeacon(),

            CapNearestSideFirst(),

            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            // NEW: Score-aware — play safe when ahead
            ScoreAwarePush(),
            ContestBeacon(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — disciplined finisher
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

            // NEW: Defend beacons more (was losing them)
            DefendOwnedBeacon(),
            CapNearestUnowned(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            // NEW: Score-aware play
            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);
}
