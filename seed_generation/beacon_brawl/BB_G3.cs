// ─────────────────────────────────────────────────────────────────────────────
// BB_G3.cs — Generation 3 Beacon Brawl teams (evolved from gen_002 analysis)
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 2 Results:
//   1. Blitz_Runners  1170 ELO (15-0) — PERFECT, unbeatable kill snowball
//   2. Bastion_Guard  1001 ELO (7-8)  — risen from worst to competitive
//   3. Iron_Vanguard   984 ELO (8-7)  — stagnant, 0-5 vs Blitz
//   4. Shadow_Flankers  845 ELO (0-15) — COLLAPSED, retreat strategy killed them
//
// Gen 3 Strategy: Counter-Blitz meta
//   All teams adopt aggressive kill+cap style (the winning meta).
//   Shadow gets COMPLETE rework — adopt Blitz-like aggression.
//   Focus on coordinating both pawns to focus-fire same target.
//   Contest enemy beacons more aggressively to freeze Blitz scoring.

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BB_G3
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
    // 1. Iron_Vanguard — "Anti-Blitz Brawler"
    //    Gen 2 was 0-5 vs Blitz. Need to disrupt kill snowball:
    //    more parry usage, better kiting, contest Blitz beacons.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef IronVanguard() => new(
    [
        // Pawn 0: Grappler — parry-heavy anti-aggression
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            // Strong defensive parry
            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn — critical tempo play
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Defend beacons aggressively
            DefendOwnedBeacon(),

            // Engage enemies near beacons (fight for territory)
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Contest enemy beacons (freeze their scoring)
            ContestBeacon(),

            CapNearestSideFirst(),

            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — anti-aggression fire support
        [Sel(
            HealAtBase(0.25f),
            ParryReact(),
            ExploitVulnerable(),

            // Strong kiting — keep Blitz grapplers at bay
            GunnerKite(300),

            // Finish low HP enemies for kill bonus
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),

            // Fire from beacon
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),

            // Contest enemy beacons
            ContestBeacon(),

            CapNearestUnowned(),

            Seq("Reach center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Shadow_Flankers — "Kill Flanker" (COMPLETE REWORK)
    //    Gen 2 was 0-15. Retreat strategy was catastrophic.
    //    New identity: Blitz-style aggression with flanking angles.
    //    Both pawns target the same enemy for coordinated kills.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef ShadowFlankers() => new(
    [
        // Pawn 0: Grappler — aggressive flanking hunter
        [Sel(
            HookStateMachine(),
            HealAtBase(0.15f), // Very aggressive — stay in fight

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Hunt enemies aggressively (like Blitz)
            Seq("Hunt enemies", C("nearest_enemy_dist < 500"),
                GrapplerEngage()),

            // Cap during respawn windows
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center for kills
            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),

            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy") // Always hunting
        )],

        // Pawn 1: Gunner — aggressive finisher (Blitz-style)
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(200),

            // Finish low enemies — coordinated kills with grappler
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

            // Contest enemy beacons
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

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Bastion_Guard — "Fortress Striker"
    //    Gen 2 rose to 7-8, trending up. Refining the combat+defend balance.
    //    Better center control once sides are secured.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BastionGuard() => new(
    [
        // Pawn 0: Grappler — strike from defended positions
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Engage enemies near owned beacons (defend while fighting)
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Hunt close enemies when no beacons to defend
            Seq("Hunt close", C("nearest_enemy_dist < 350"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Center push with platform advantage
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            Seq("Platform engage", C("in_beacon_center == 1"), C("nearest_enemy_dist < 400"),
                GrapplerPlatformDive()),

            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — kill support + beacon hold
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // Finish low HP targets
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),

            // Fire from beacon position
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Ricochet center support
            Seq("Support center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_wall_bounce")),

            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Blitz_Runners — "Apex Predator"
    //    Gen 2 was PERFECT 15-0. Optimizing the winning formula:
    //    tighter hunt range, better parry timing, stronger beacon contest.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BlitzRunners() => new(
    [
        // Pawn 0: Grappler — optimized hunter
        [Sel(
            HookStateMachine(),
            HealAtBase(0.18f),

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Tighter hunt range — don't overextend
            Seq("Hunt enemies", C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),

            // Contest before capping (freeze enemy scoring)
            ContestBeacon(),

            CapNearestSideFirst(),

            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — kill finisher + beacon flipper
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(200),

            // Finish low enemies for kill chain
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
