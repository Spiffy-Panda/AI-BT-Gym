// ─────────────────────────────────────────────────────────────────────────────
// BB_G4.cs — Generation 4 Beacon Brawl teams (evolved from gen_003 analysis)
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 3 Results (most competitive generation yet!):
//   1. Shadow_Flankers 1049 ELO (10-5)  — Blitz-rework = massive success
//   2. Blitz_Runners   1012 ELO (8-7)   — dethroned, deaths rising
//   3. Bastion_Guard    972 ELO (6-9)   — competitive middle pack
//   4. Iron_Vanguard    967 ELO (6-9)   — defensive style loses to aggression
//
// Gen 4 Strategy: Convergent evolution — all teams now aggressive
//   Iron_Vanguard: Drop defensive identity, adopt kill-first with parry edge
//   Shadow_Flankers: Refine G3 success, optimize heal/engage thresholds
//   Bastion_Guard: More aggressive with beacon fire support combo
//   Blitz_Runners: Evolve: smarter target selection, better parry defense

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BB_G4
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
    // 1. Iron_Vanguard — "Parry Assassin"
    //    Gen 3 was 1-4 vs both aggression teams. Dropping defensive focus.
    //    New identity: parry-based kill chains. Parry → lockout → free kills.
    //    Kill-first with parry as the differentiator.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef IronVanguard() => new(
    [
        // Pawn 0: Grappler — parry assassin
        [Sel(
            HookStateMachine(),
            HealAtBase(0.15f), // Aggressive — like the winners

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // HUNT — be aggressive like Shadow/Blitz
            Seq("Hunt enemies", C("nearest_enemy_dist < 450"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center for fights
            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — kill support with beacon pressure
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // Finish low enemies
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),
            Seq("Chase low HP", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 300"),
                Act("move_toward_nearest_enemy")),

            // Fire from beacon
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            ContestBeacon(),
            CapNearestUnowned(),

            Seq("Reach center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Shadow_Flankers — "Refined Predator"
    //    Gen 3 champion at 10-5. Refining: tighter engage windows,
    //    better heal timing, and adding beacon fire support combo.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef ShadowFlankers() => new(
    [
        // Pawn 0: Grappler — optimized hunter
        [Sel(
            HookStateMachine(),
            HealAtBase(0.18f), // Slightly higher than 0.15 — survive a bit more

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Hunt enemies
            Seq("Hunt enemies", C("nearest_enemy_dist < 450"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center
            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ContestBeacon(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — fire + cap combo
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

            // Fire from beacon position — hold + shoot
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

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

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Bastion_Guard — "Beacon Brawler"
    //    Gen 3 was 6-9 but competitive. Going full aggression:
    //    fight on beacons, cap after kills, contest everything.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BastionGuard() => new(
    [
        // Pawn 0: Grappler — fight on the point
        [Sel(
            HookStateMachine(),
            HealAtBase(0.15f), // Max aggression

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Hunt aggressively
            Seq("Hunt enemies", C("nearest_enemy_dist < 450"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center
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

        // Pawn 1: Gunner — aggressive fire + cap
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
            Seq("Chase low HP", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 300"),
                Act("move_toward_nearest_enemy")),

            // Fire from beacon
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            GunnerSnipe(800, 350),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            ContestBeacon(),
            CapNearestSideFirst(),

            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ScoreAwarePush(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Blitz_Runners — "Evolved Blitz"
    //    Gen 3 was 8-7, deaths rose to 8.3. Need to evolve:
    //    smarter engagement (don't chase into disadvantage),
    //    better parry defense, and beacon-anchored hunting.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BlitzRunners() => new(
    [
        // Pawn 0: Grappler — smart hunter (fight near beacons)
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f), // More survivable — stop dying so much

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Cap during respawn
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            // Engage near beacons — fight for territory, not just kills
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Hunt when beacons are secure
            Seq("Hunt when secure", C("owned_beacon_count >= 2"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            ContestBeacon(),

            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            GrapplerPlatformDive(),
            ScoreAwarePush(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy")
        )],

        // Pawn 1: Gunner — disciplined finisher
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),
            ExploitVulnerable(),

            // Stronger kiting — survive better
            GunnerKite(250),

            // Finish low enemies
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
